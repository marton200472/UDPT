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
        private readonly TrackerDbContext dbContext;
        private readonly TimeSpan _announceInterval;
        private Thread? _thread;
        

        private bool keepRunning;
        private bool _ipv6Enabled;

        private bool _isRunning;
        public bool IsRunning => _isRunning;

        internal DataHandlerThread(IServiceProvider srvcProvider, ConcurrentQueue<RequestMessage> reqQueue, ConcurrentQueue<ResponseMessage> respQueue, ConcurrentQueue<ConnectionId> connIds,ConcurrentQueue<string> torrentUpdateQueue, IPAddress[] localAddresses,IPAddress publicIPv4Address, TimeSpan announceInterval)
        {
            _requestQueue = reqQueue;
            _responseQueue = respQueue;
            _connectionIds = connIds;
            _torrentUpdateQueue=torrentUpdateQueue;
            _ipv4addr = localAddresses.Single(x=>x is not null&&x.AddressFamily==AddressFamily.InterNetwork);
            _pubipv4addr = publicIPv4Address;
            _ipv6addr = localAddresses.SingleOrDefault(x=>x is not null&&x.AddressFamily==AddressFamily.InterNetworkV6);

            _ipv6Enabled = _ipv6addr is not null;

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

            //Check invalid connection ID
            if (!_connectionIds.Any(x => x.id == annreq.ConnectionId))
            {
                return HandleError(request, "Invalid connection id.");
            }

            //check if IPv6 is enabled
            if(hostipaddress.AddressFamily==AddressFamily.InterNetworkV6&&!_ipv6Enabled)
                throw new AnnounceException("IPv6 is not enabled on this tracker.");

            //Check if client sent port, and update it if necessary
            if (annreq.Port == 0)
            {
                annreq.Port = (ushort)host.Port;
            }

            var ips=DetermineIPAddresses(hostipaddress);

            Peer? peer;
            
            peer=dbContext.Peers.Where(x => x.PeerId == annreq.PeerId && x.InfoHash == annreq.InfoHash).AsEnumerable().Where(x=>x.AddressFamily==annreq.AddressFamily).SingleOrDefault();
            
            IEnumerable<Peer> peerstosend;
            int seeders;
            int leechers;
            
            if (annreq.Event==AnnounceRequest.AnnounceEvent.Stopped)
            {
                if (peer is not null)
                {
                    dbContext.Peers.Remove(peer);
                    dbContext.SaveChanges();
                }
                peerstosend=new Peer[0];
                seeders=0;
                leechers=0;
            }
            else
            {
                if(peer is null){
                    peer=new Peer(annreq.PeerId,ips.publicaddress,annreq.Port,annreq.InfoHash,ips.privateaddress);
                    dbContext.Peers.Add(peer);
                }
                else
                {
                    peer.Refresh();
                }

                if (annreq.Left>0)
                {
                    peer.Status=Peer.PeersStatus.Leech;
                }
                else
                {
                    peer.Status=Peer.PeersStatus.Seed;
                }

                dbContext.SaveChanges();

                var peers=dbContext.Peers.Where(x=>x.InfoHash==annreq.InfoHash).ToArray();

                peerstosend=peers.Where(x=>x.AddressFamily==annreq.AddressFamily).Except(new Peer[]{peer}).Take((int)annreq.WantedClients).ToArray();

                seeders=peers.Count(x=>x.Status==Peer.PeersStatus.Seed);
                leechers=peers.Count(x=>x.Status==Peer.PeersStatus.Leech);
            }

            _torrentUpdateQueue.Enqueue(annreq.InfoHash);

            return annreq.GetResponseBytes(_announceInterval,leechers,seeders,peerstosend);
            
        }

        private (IPAddress publicaddress, IPAddress? privateaddress) DetermineIPAddresses(IPAddress source){
            if (IPAddress.IsLoopback(source))
            {
                if (source.AddressFamily==AddressFamily.InterNetwork)
                {
                    return (_pubipv4addr, _ipv4addr);
                }
                else{
                    return (_ipv6addr!,null);
                }
            }
            else if (source.AddressFamily==AddressFamily.InterNetwork&&source.IsInSubnet(_ipv4addr+"/"+Helpers.GetPrefixLengthForLocalAddress(_ipv4addr) ))
            {
                return (_pubipv4addr,source);
            }
            return (source,null);
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
