using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Net.Sockets;
using System.Net;
using Microsoft.EntityFrameworkCore;
using System.Net.NetworkInformation;
using Microsoft.Extensions.Hosting;
using BTTracker.UDPMessages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Shared.Models;
using Shared;
using System.Collections.Concurrent;

namespace BTTracker
{
    internal class UdpTracker : IHostedService
    {
        private static Random R = new Random();

        private ILogger<UdpTracker> _logger;


        private TimeSpan AnnounceInterval;

        private ConcurrentQueue<ConnectionId> ConnectionIds = new();

        private System.Timers.Timer DeleteTimer;

        private IPAddress PublicIPv4Address;
        private ConcurrentQueue<RequestMessage> Requests = new ConcurrentQueue<RequestMessage>();
        private bool Running;
        private IServiceProvider ServiceProvider;
        private TrackerConfig TrackerConfig;

        private TrackerDbContext _announceDbContext;

        public UdpTracker(TrackerConfig config,IServiceProvider serviceProvider, ILogger<UdpTracker> logger)
        {
            AnnounceInterval = config.AnnounceInterval;
            TrackerConfig = config;
            ServiceProvider = serviceProvider;
            if (config==null) config=TrackerConfig.Default;

            _announceDbContext = serviceProvider.CreateScope().ServiceProvider.GetService<TrackerDbContext>()!;

            _logger = logger;

            DeleteTimer = new System.Timers.Timer(30000) { AutoReset = true };
            DeleteTimer.Elapsed += DeleteTimer_Elapsed;
        }

        private enum Action
        {
            Connect = 0, Announce = 1, Scrape = 2, Error = 3
        }

        public int GetPrefixLengthForLocalAddress(IPAddress sourceaddress)
        {
            foreach (NetworkInterface item in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (item.OperationalStatus == OperationalStatus.Up)
                {
                    foreach (UnicastIPAddressInformation ip in item.GetIPProperties().UnicastAddresses)
                    {
                        if (ip.Address.Equals(sourceaddress))
                        {
                            return ip.PrefixLength;
                        }
                    }
                }
            }
            throw new Exception("Interface not found with provided address!");
        }

        private void SynchronWorker()
        {

        }

        private async Task UpdateTorrents()
        {

        }

        public Task StartAsync(CancellationToken token)
        {
            PublicIPv4Address = GetPublicIPv4Address();
            _logger.LogInformation("Public IP address: {0}", PublicIPv4Address);
            //foreach (var endpoint in TrackerConfig.Endpoints)
            //{
            //    var client=new UdpClient(endpoint);
            //    var tokensource = new CancellationTokenSource();
            //    Clients.Add((client, tokensource));
            //    _ = Listen(client,tokensource.Token);
            //    _logger.LogInformation("UDP Tracker running on {0} port {1}", endpoint.Address, endpoint.Port);
            //}
            foreach (var endpoint in TrackerConfig.Endpoints)
            {
                var client = new UdpClient(endpoint);
                client.Client.Blocking = false;
                client.BeginReceive(OnMessageReceived, client);
                _logger.LogInformation("UDP Tracker running on {0} port {1}", endpoint.Address, endpoint.Port);
            }
            Running = true;
            new Thread(HandleMessages).Start();
            DeleteTimer.Start();
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken token)
        {
            Running = false;
            DeleteTimer.Stop();
            if (!Requests.IsEmpty)
            {
                _logger.LogInformation("Waiting for pending requests to complete...");
            }
            while (!Requests.IsEmpty)
            {
                await Task.Delay(100);
            }

        }

        private void DeleteExpiredIds()
        {
            int deletedids = 0;
            while (ConnectionIds.TryPeek(out var result))
            {
                if (result.exp < DateTime.Now)
                {
                    ConnectionIds.TryDequeue(out _);
                }
                else
                {
                    break;
                }
            }
            if (deletedids > 0)
                _logger.LogDebug("Deleted {0} expired connectionids.", deletedids);

        }

        private void DeleteExpiredPeers()
        {
            using (var scope=ServiceProvider.CreateScope())
            {
                var context = scope.ServiceProvider.GetService<TrackerDbContext>()!;
                var peerstoremove = context.Peers.Where(x => (x.TimeStamp + AnnounceInterval) < DateTime.Now);
                context.Peers.RemoveRange(peerstoremove.ToArray());
                context.SaveChanges();
            }
        }

        private void DeleteTimer_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            DeleteExpiredIds();
            DeleteExpiredPeers();
        }
        private Action GetAction(byte[] request)
        {
            int i = request.DecodeInt(8);
            return (Action)i;
        }

        private IPAddress? GetIPv6Address()
        {
            using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.IP))
            {
                try
                {
                    socket.Connect("2606:4700:4700::1111", 65530);
                }
                catch
                {
                    return null;
                }

                IPEndPoint? endPoint = socket.LocalEndPoint as IPEndPoint;
                if (endPoint == null) return IPAddress.Loopback;
                return endPoint.Address;
            }
        }

        private IPAddress GetLocalIPv4Address()
        {
            using (Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.IP))
            {
                socket.Connect("1.1.1.1", 65530);
                IPEndPoint? endPoint = socket.LocalEndPoint as IPEndPoint;
                if (endPoint == null) return IPAddress.Loopback;
                return endPoint.Address;
            }
        }

        private IPAddress GetPublicIPv4Address()
        {
            string externalIpString = new HttpClient().GetStringAsync("http://icanhazip.com").GetAwaiter().GetResult().Replace("\\r\\n", "").Replace("\\n", "").Trim();
            return IPAddress.Parse(externalIpString);
        }

        private byte[] HandleAnnounce(byte[] request, IPEndPoint host)
        {
            IPAddress hostipaddress = host.Address;
            AnnounceRequest annreq = AnnounceRequest.FromByteArray(request, hostipaddress.AddressFamily);

            if (annreq.Port == 0)
            {
                annreq.Port = (ushort)host.Port;
            }


            if (!ConnectionIds.Any(x => x.id == annreq.ConnectionId))
            {
                return HandleError(request, "Invalid connection id.");
            }

            IPAddress? localipaddress = null, publicipaddress;

            if (IPAddress.IsLoopback(hostipaddress))
            {
                if (hostipaddress.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    publicipaddress = GetIPv6Address();
                }
                else
                {
                    localipaddress = GetLocalIPv4Address();
                    publicipaddress = new IPAddress(PublicIPv4Address.GetAddressBytes());
                }
            }
            else if (hostipaddress.IsInSubnet(GetLocalIPv4Address() + "/" + GetPrefixLengthForLocalAddress(GetLocalIPv4Address())))
            {
                localipaddress = hostipaddress;
                publicipaddress = new IPAddress(PublicIPv4Address.GetAddressBytes());
            }
            else
            {
                publicipaddress = hostipaddress;
            }


            if (publicipaddress == null)
            {
                return HandleError(request, "No valid IP address.");
            }

            Peer? currentpeer = _announceDbContext.Peers.Where(x => x.PeerId == annreq.PeerId && x.InfoHash == annreq.InfoHash).AsEnumerable().SingleOrDefault(x=> x.AddressFamily == annreq.AddressFamily);
            if (currentpeer is null)
            {
                Peer newPeer = new Peer(annreq.PeerId, publicipaddress, annreq.Port, annreq.InfoHash, localipaddress);
                if (annreq.Left > 0) newPeer.Status = Peer.PeersStatus.Leech;
                else newPeer.Status = Peer.PeersStatus.Seed;

                _announceDbContext.Peers.Add(newPeer);
                currentpeer = newPeer;

                Console.WriteLine("Added peer: " + publicipaddress.ToString() + ":" + annreq.Port);
            }
            else
            {
                currentpeer.Refresh();
            }
            

            var allpeers = _announceDbContext.Peers.Where(x => x.InfoHash == annreq.InfoHash).ToArray();


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

            Task.Run(() => { _ = UpdateTorrent(annreq.InfoHash, allpeers); });

            return annreq.GetResponseBytes(AnnounceInterval, leechers, seeders, peerstosend);
        }

        private byte[] HandleConnect(byte[] request)
        {
            ConnectionRequest connreq = ConnectionRequest.FromByteArray(request);
            long connectionid = R.NextInt64();
            ConnectionIds.Enqueue(new ConnectionId(DateTime.Now + TimeSpan.FromMinutes(2), connectionid));
            return connreq.GetResponseBytes(connectionid);
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

        private void HandleMessages()
        {
            while (Running)
            {
                if (Requests.TryDequeue(out var request))
                {
                    Console.WriteLine(String.Join(", ",request.data));
                    var responsebytes = HandleRequest(request.remoteEP, request.data);
                    if (responsebytes == null)
                    {
                        continue;
                    }

                    request.client.Send(responsebytes,request.remoteEP);
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine(GetAction(request.data)+" complete in "+(DateTime.Now-request.received).TotalMilliseconds+"ms");
                    _announceDbContext.SaveChanges();
                }
                else
                {
                    Thread.Sleep(10);
                }
            }
        }

        private byte[]? HandleRequest(IPEndPoint remoteEp, byte[] data)
        {
            if (data.Length < 16)
            {
                return null;
            }
            Action action = GetAction(data);
            byte[] response;
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
            return response;
        }

        private void OnMessageReceived(IAsyncResult result)
        {
            var client = (UdpClient)result.AsyncState!;
            IPEndPoint? ep = null;
            byte[] data = client.EndReceive(result, ref ep);
            Requests.Enqueue(new RequestMessage(client,ep!,data,DateTime.Now));
            client.BeginReceive(OnMessageReceived, client);
        }
        private async Task UpdateTorrent(string info_hash, IEnumerable<Peer> peers)
        {
            using (var scope=ServiceProvider.CreateScope())
            {
                var dbcontext = scope.ServiceProvider.GetService<TrackerDbContext>();
                Torrent? torrenttoupdate = await dbcontext.Torrents.FindAsync(info_hash);
                if (torrenttoupdate is null)
                {
                    torrenttoupdate = new Torrent(info_hash);
                    await dbcontext.Torrents.AddAsync(torrenttoupdate);
                }
                torrenttoupdate.Seeders = peers.Count(x => x.Status == Peer.PeersStatus.Seed);
                torrenttoupdate.Leechers = peers.Count(x => x.Status == Peer.PeersStatus.Leech);
                await dbcontext.SaveChangesAsync();
            }
            
        }
        internal record struct ConnectionId(DateTime exp,long id);

        internal record struct RequestMessage(UdpClient client, IPEndPoint remoteEP, byte[] data, DateTime received);
    }
}
