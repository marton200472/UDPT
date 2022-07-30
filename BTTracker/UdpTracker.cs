using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Net;
using Microsoft.EntityFrameworkCore;
using System.Net.NetworkInformation;
using Microsoft.Extensions.Hosting;
using BTTracker.UDPMessages;
using Microsoft.Extensions.Logging;

namespace BTTracker
{
    internal class UdpTracker : IHostedService
    {
        private List<ConnectionId> ConnectionIds = new List<ConnectionId>();
        private List<Peer> Peers = new List<Peer>();
        private object _peerLock=new();
        private object _connectionIdLock = new();
        private object DbLock = new();
        private static Random R = new Random();
        private System.Timers.Timer DeleteTimer;


        private TrackerDbContext DbContext;

        private TimeSpan AnnounceInterval;

        private IPAddress PublicIPv4Address;

        private TrackerConfig TrackerConfig;

        private List<(UdpClient, CancellationTokenSource)> Clients = new List<(UdpClient, CancellationTokenSource)>();

        private ILogger<UdpTracker> _logger;

        private List<Task> ResponseTasks = new List<Task>();

        public UdpTracker(TrackerConfig config,TrackerDbContext dbContext, ILogger<UdpTracker> logger)
        {
            AnnounceInterval = config.AnnounceInterval;
            TrackerConfig = config;
            DbContext = dbContext;
            DbContext.Database.Migrate();
            if  (config==null) config=TrackerConfig.Default;

            PublicIPv4Address = GetPublicIPv4Address();

            _logger = logger;

            DeleteTimer = new System.Timers.Timer(30000) { AutoReset = true };
            DeleteTimer.Elapsed += DeleteTimer_Elapsed;
        }

        private void DeleteTimer_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            DeleteExpiredIds();
            DeleteExpiredPeers();
        }

        public void Start()
        {
            PublicIPv4Address = GetPublicIPv4Address();
            foreach (var endpoint in TrackerConfig.Endpoints)
            {
                var client=new UdpClient(endpoint);
                var tokensource = new CancellationTokenSource();
                Clients.Add((client, tokensource));
                _ = Listen(client,tokensource.Token);
                _logger.LogInformation("UDP Tracker running on {0} port {1}", endpoint.Address, endpoint.Port);
            }
            DeleteTimer.Start();
            
        }

        public async Task StopAsync(CancellationToken token)
        {
            foreach (var client in Clients)
            {
                client.Item2.Cancel();
                
            }
            DeleteTimer.Stop();
            if (ResponseTasks.Count(x=>!x.IsCompleted)>0&&!token.IsCancellationRequested)
            {
                _logger.LogInformation("Waiting for {0} tasks...", ResponseTasks.Count);
                await Task.WhenAll(ResponseTasks);
            }
            
            foreach (var client in Clients)
            {
                client.Item1.Close();
                client.Item1.Dispose();
            }
        }

        private enum Action
        {
            Connect=0, Announce=1, Scrape=2, Error=3
        }

        private void DeleteExpiredIds()
        {
            int deletedids = 0;
            lock(_connectionIdLock) {
                for (int i = 0; i < ConnectionIds.Count; )
                {
                    if (ConnectionIds[i].exp < DateTime.Now)
                    {
                        ConnectionIds.RemoveAt(i);
                        deletedids++;
                    }
                    else i++;
                }
            }
            if(deletedids>0)
                _logger.LogDebug("Deleted {0} expired connectionids.",deletedids);
            
        }

        private void DeleteExpiredPeers()
        {
            DateTime lastvalidtimestamp = DateTime.Now - AnnounceInterval;
            for (int i = 0; i < Peers.Count; i++)
            {
                if (Peers[i].TimeStamp<lastvalidtimestamp)
                {
                    Peers.RemoveAt(i);
                    i--;
                }
            }
        }

        private async Task Listen(UdpClient client,CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                var packet = await client.ReceiveAsync(token);
                ResponseTasks.Add(Task.Run(() => HandleRequest(client,packet)));
            }
        }

        private async Task HandleRequest(UdpClient client, UdpReceiveResult request)
        {
            byte[] requestdata = request.Buffer;
            if (requestdata.Length < 16)
            {
                return;
            }
            IPEndPoint host = request.RemoteEndPoint;
            Action action = GetAction(requestdata);
            byte[] response;
            switch (action)
            {
                case Action.Connect:
                    response = HandleConnect(requestdata);
                    break;
                case Action.Announce:
                    response = HandleAnnounce(requestdata, host);
                    break;
                case Action.Scrape:
                    response = HandleError(requestdata,"Scrape not supported yet.");
                    break;
                default:
                    response = HandleError(requestdata,"Unknown error happened.");
                    break;
            }

            await client.SendAsync(response, response.Length, host);
        }

        private byte[] HandleConnect(byte[] request)
        {
            ConnectionRequest connreq = ConnectionRequest.FromByteArray(request);
            long connectionid = R.NextInt64();
            lock (_connectionIdLock)
            {
                ConnectionIds.Add(new ConnectionId(DateTime.Now + TimeSpan.FromMinutes(2), connectionid));
            }
            return connreq.GetResponseBytes(connectionid);
        }
        
        private byte[] HandleAnnounce(byte[] request, IPEndPoint host)
        {
            IPAddress hostipaddress = host.Address;
            AnnounceRequest annreq = AnnounceRequest.FromByteArray(request,hostipaddress.AddressFamily);

            if (annreq.Port == 0)
            {
                annreq.Port = (ushort)host.Port;
            }

            lock (_connectionIdLock)
            {
                if (!ConnectionIds.Any(x=>x.id==annreq.ConnectionId))
                {
                    return HandleError(request, "Invalid connection id.");
                }
            }
            
            IPAddress? localipaddress=null, publicipaddress;

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
                publicipaddress=hostipaddress;
            }


            if (publicipaddress==null)
            {
                return HandleError(request,"No valid IP address.");
            }
            Peer[] allpeers;
            lock (_peerLock)
            {
                Peer? currentpeer = Peers.SingleOrDefault(x => (x.LocalAddress.Equals(localipaddress) || x.Address.Equals(publicipaddress)) && x.InfoHash == annreq.InfoHash && x.Port == annreq.Port);
                if (currentpeer == null)
                {
                    Peer newPeer = new Peer(publicipaddress, annreq.Port, annreq.InfoHash, localipaddress);
                    if (annreq.Left > 0) newPeer.Status = Peer.PeersStatus.Leech;
                    else newPeer.Status = Peer.PeersStatus.Seed;

                    Peers.Add(newPeer);


                    Console.WriteLine("Added peer: " + publicipaddress.ToString() + ":" + annreq.Port);
                }
                else
                {
                    currentpeer.Refresh();
                }

                allpeers = Peers.Where(x => x.AddressFamily == host.AddressFamily && x.InfoHash == annreq.InfoHash).ToArray();
            }
            
            var peerstosend = allpeers.Take(annreq.WantedClients).Select(x =>
            {
                if (localipaddress == null || x.LocalAddress == null)
                {
                    return new Peer(x.Address,x.Port,annreq.InfoHash);
                }
                else
                {
                    return new Peer(x.LocalAddress, x.Port, annreq.InfoHash);
                }
            }).ToArray();

            int leechers=allpeers.Where(x=>x.Status==Peer.PeersStatus.Leech).Count();
            int seeders=allpeers.Where(x => x.Status == Peer.PeersStatus.Seed).Count();

            return annreq.GetResponseBytes(AnnounceInterval,leechers,seeders,peerstosend);
        }

        private byte[] HandleError(byte[] request, string message)
        {
            int transactionid = request.DecodeInt(12);
            byte[] msg = message.GetBigendianBytes();
            byte[] response = new byte[8+msg.Length];
            BitConverter.GetBytes(3).CopyTo(response, 0);
            BitConverter.GetBytes(transactionid).CopyTo(response, 4);
            msg.CopyTo(response, 8);
            return response;
        }

        private IPAddress GetPublicIPv4Address()
        {
            string externalIpString = new HttpClient().GetStringAsync("http://icanhazip.com").GetAwaiter().GetResult().Replace("\\r\\n", "").Replace("\\n", "").Trim();
            return IPAddress.Parse(externalIpString);
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

        private Action GetAction(byte[] request)
        {
            int i = request.DecodeInt(8);
            return (Action)i;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Start();
            return Task.FromResult(true);
        }

        internal record struct ConnectionId(DateTime exp,long id);
    }
}
