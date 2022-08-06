using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Shared;
using Shared.Models;
using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using BTTracker.UDPMessages;
using System.Net.Sockets;

namespace BTTracker
{
    internal class DataHandlerThread
    {
        private static Random R = new Random();


        private readonly ConcurrentQueue<RequestMessage> _requestQueue;
        private readonly ConcurrentQueue<ResponseMessage> _responseQueue;
        private readonly ConcurrentQueue<ConnectionId> _connectionIds;
        private readonly ConcurrentQueue<string> _torrentUpdateQueue;
        private readonly IPAddress _ipv4addr;
        private readonly IPAddress _pubipv4addr;
        private readonly IPAddress? _ipv6addr;
        private readonly IPAddress? _pubipv6addr;
        private readonly TrackerDbContext dbContext;
        private readonly TimeSpan _announceInterval;
        private Thread? _thread;
        

        private bool keepRunning;
        private bool _ipv6Enabled;

        private bool _isRunning;
        public bool IsRunning => _isRunning;

        internal DataHandlerThread(IServiceProvider srvcProvider, ConcurrentQueue<RequestMessage> reqQueue, ConcurrentQueue<ResponseMessage> respQueue, ConcurrentQueue<ConnectionId> connIds,ConcurrentQueue<string> torrentUpdateQueue, IPAddress[] localEps,IPAddress[] publicAddresses, TimeSpan announceInterval)
        {
            _requestQueue = reqQueue;
            _responseQueue = respQueue;
            _connectionIds = connIds;
            _torrentUpdateQueue=torrentUpdateQueue;
            _ipv4addr = localEps.Single(x=>x.AddressFamily==AddressFamily.InterNetwork);
            _pubipv4addr = publicAddresses.Single(x=>x.AddressFamily==AddressFamily.InterNetwork);
            _ipv6addr = localEps.SingleOrDefault(x=>x.AddressFamily==AddressFamily.InterNetworkV6);
            _pubipv6addr = publicAddresses.SingleOrDefault(x=>x.AddressFamily==AddressFamily.InterNetworkV6);

            _ipv6Enabled = _ipv6addr is not null && _pubipv6addr is not null;

            _announceInterval =announceInterval;
            dbContext = srvcProvider.CreateScope().ServiceProvider.GetService<TrackerDbContext>()!;
        }

        internal bool Start()
        {
            if (IsRunning)
            {
                return false;
            }
            keepRunning = true;
            _thread = new Thread(HandleMessages);
            _thread.Start();
            return true;
        }

        internal bool Stop()
        {
            if (!IsRunning)
            {
                return false;
            }
            keepRunning=false;
            return true;
        }

        private void HandleMessages()
        {
            _isRunning = true;
            while (!_requestQueue.IsEmpty || keepRunning)
            {
                if (_requestQueue.TryDequeue(out var request))
                {
                    var responsebytes = HandleRequest(request.remoteEP, request.data);
                    if (responsebytes == null)
                    {
                        continue;
                    }
                    _responseQueue.Enqueue(new ResponseMessage(request.client,request.remoteEP, responsebytes));

                    
                }
                else
                {
                    Thread.Sleep(10);
                }
            }
            _isRunning = false;
        }

        private byte[]? HandleRequest(IPEndPoint remoteEp, byte[] data)
        {
            if (data.Length < 16)
            {
                return null;
            }
            if (remoteEp.AddressFamily==AddressFamily.InterNetworkV6&&!_ipv6Enabled)
            {
                return HandleError(data, "IPv6 not supported.");
            }
            Action action = Helpers.GetAction(data);
            byte[] response;
            try
            {
                switch (action)
                {
                    case Action.Connect:
                        response = HandleConnect(data);
                        break;
                    case Action.Announce:
                        response = HandleAnnounce(data, remoteEp);
                        break;
                    case Action.Scrape:
                        response = HandleError(data, "Scrape not supported yet.");
                        break;
                    default:
                        response = HandleError(data, "Unknown error happened.");
                        break;
                }
            }
            catch (Exception )
            {
                response = HandleError(data, "");
            }
            
            return response;
        }

        private byte[] HandleConnect(byte[] request)
        {
            ConnectionRequest connreq = ConnectionRequest.FromByteArray(request);
            long connectionid = R.NextInt64();
            _connectionIds.Enqueue(new ConnectionId(DateTime.Now + TimeSpan.FromMinutes(2), connectionid));
            return connreq.GetResponseBytes(connectionid);
        }

        private byte[] HandleAnnounce(byte[] request, IPEndPoint host)
        {
            IPAddress hostipaddress = host.Address;
            AnnounceRequest annreq = AnnounceRequest.FromByteArray(request, hostipaddress.AddressFamily);

            if (annreq.Port == 0)
            {
                annreq.Port = (ushort)host.Port;
            }


            if (!_connectionIds.Any(x => x.id == annreq.ConnectionId))
            {
                return HandleError(request, "Invalid connection id.");
            }

            IPAddress? localipaddress = null, publicipaddress;

            if (IPAddress.IsLoopback(hostipaddress))
            {
                if (hostipaddress.AddressFamily==AddressFamily.InterNetwork)
                {
                    publicipaddress = _pubipv4addr;
                }
                else
                {
                    publicipaddress= _pubipv6addr;
                }
                
            }
            else if (hostipaddress.IsInSubnet(_ipv4addr + "/" + Helpers.GetPrefixLengthForLocalAddress(_ipv4addr)))
            {
                localipaddress = hostipaddress;
                publicipaddress = new IPAddress(_pubipv4addr.GetAddressBytes());
            }
            else
            {
                publicipaddress = hostipaddress;
            }


            if (publicipaddress == null)
            {
                return HandleError(request, "No valid IP address.");
            }

            try
            {
                Peer? currentpeer = dbContext.Peers.Where(x => x.PeerId == annreq.PeerId && x.InfoHash == annreq.InfoHash).AsEnumerable().SingleOrDefault(x => x.AddressFamily == annreq.AddressFamily);
                if (annreq.Event==AnnounceRequest.AnnounceEvent.Stopped)
                {
                    if (currentpeer is not null)
                    {
                        dbContext.Peers.Remove(currentpeer);
                        dbContext.SaveChanges();
                        
                    }
                    return annreq.GetResponseBytes(_announceInterval, 0, 0, new List<Peer>());
                }
                
                if (currentpeer is null)
                {
                    Peer newPeer = new Peer(annreq.PeerId, publicipaddress, annreq.Port, annreq.InfoHash, localipaddress);
                    if (annreq.Left > 0) newPeer.Status = Peer.PeersStatus.Leech;
                    else newPeer.Status = Peer.PeersStatus.Seed;

                    dbContext.Peers.Add(newPeer);
                    currentpeer = newPeer;
                }
                else
                {
                    currentpeer.Refresh();
                }
                dbContext.SaveChanges();
                _torrentUpdateQueue.Enqueue(annreq.InfoHash);
                var allpeers = dbContext.Peers.Where(x => x.InfoHash == annreq.InfoHash).ToArray();


                var peerstosend = allpeers.Where(x => x.AddressFamily == host.AddressFamily && x.PeerId != annreq.PeerId).Take((int)annreq.WantedClients).Select(x =>
                {
                    if (localipaddress == null || x.LocalAddress == null)
                    {
                        return new Peer(x.PeerId, x.Address, x.Port, annreq.InfoHash);
                    }
                    else
                    {
                        return new Peer(x.PeerId, x.LocalAddress, x.Port, annreq.InfoHash);
                    }
                }).ToArray();

                int leechers = allpeers.Where(x => x.Status == Peer.PeersStatus.Leech).Count();
                int seeders = allpeers.Where(x => x.Status == Peer.PeersStatus.Seed).Count();

                return annreq.GetResponseBytes(_announceInterval, leechers, seeders, peerstosend);
            }
            catch (Exception e)
            {
                return HandleError(request, "Client communicated incorrectly.");
            }


            
        }

        private byte[] HandleError(byte[] request, string message)
        {
            int transactionid = request.DecodeInt(12);
            byte[] msg = message.GetBigendianBytes();
            byte[] response = new byte[8 + msg.Length];
            BitConverter.GetBytes(3).CopyTo(response, 0);
            BitConverter.GetBytes(transactionid).CopyTo(response, 4);
            msg.CopyTo(response, 8);
            return response;
        }



    }
}
