using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Net;
using Microsoft.EntityFrameworkCore;
using System.Net.NetworkInformation;

namespace BTTracker
{
    internal class UdpTracker
    {
        private List<ConnectionId> ConnectionIds = new List<ConnectionId>();
        private List<Peer> Peers = new List<Peer>();
        private object _peerLock=new();
        private object _connectionIdLock = new();
        private object _clientLock = new();
        private object DbLock = new();
        private static Random R = new Random();
        private System.Timers.Timer DeleteTimer;
        private TrackerDbContext DbContext;

        private DbContextOptions DbOptions;

        private TimeSpan AnnounceInterval;

        private IPAddress PublicIPv4Address;

        private TrackerConfig TrackerConfig;

        private List<(UdpClient, CancellationTokenSource)> Clients = new List<(UdpClient, CancellationTokenSource)>();

        public UdpTracker(TrackerConfig config,TimeSpan announceInterval,DbContextOptions dboptions =null)
        {
            AnnounceInterval = announceInterval;
            TrackerConfig = config;
            if (dboptions==null)
            {
                string connstr = @$"Data Source={new System.IO.FileInfo(System.Reflection.Assembly.GetExecutingAssembly().Location).DirectoryName}/tracker.db;".Replace('\\','/');
                dboptions = new DbContextOptionsBuilder()
                    .UseSqlite(connstr).Options;
                
            }
            DbOptions = dboptions;
            DbContext = new TrackerDbContext(dboptions);
            DbContext.Database.Migrate();
            if  (config==null) config=TrackerConfig.Default;

            PublicIPv4Address = GetPublicIPv4Address();


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
                Console.WriteLine($"UDP Tracker running on {endpoint.Address} port {endpoint.Port}");
            }
            DeleteTimer.Start();
            
        }

        public void Stop()
        {
            foreach (var client in Clients)
            {
                client.Item2.Cancel();
                client.Item1.Close();
                client.Item1.Dispose();
            }
            DeleteTimer.Stop();
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
                Console.WriteLine(DateTime.Now+"\t[Expired ID Deleter]: Deleted "+deletedids+" connectionids.");
            
        }

        private void DeleteExpiredPeers()
        {
            DateTime bruh = DateTime.Now - AnnounceInterval;
            for (int i = 0; i < Peers.Count; i++)
            {
                if (Peers[i].TimeStamp<bruh)
                {
                    Peers.RemoveAt(i);
                    i--;
                }
            }
            //using (TrackerDbContext ctx=new TrackerDbContext(DbOptions))
            //{
            //    Peer[] peerstodelete = ctx.Peers.AsEnumerable().Where(x => x.TimeStamp < DateTime.Now - AnnounceInterval).ToArray();
            //    ctx.Peers.RemoveRange(peerstodelete);
            //    ctx.SaveChanges();
            //}
        }

        private async Task Listen(UdpClient client,CancellationToken token)
        {
            while (true)
            {
                var packet = await client.ReceiveAsync(token);
                if (token.IsCancellationRequested) return;
                _=Task.Run(() => HandleRequest(client,packet));
            }
        }

        private void HandleRequest(UdpClient client, UdpReceiveResult request)
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
            lock (_clientLock)
            {
                client.Send(response, response.Length, host);
            }
            
        }

        private byte[] HandleConnect(byte[] request)
        {
            int transactionid = request.DecodeInt(12);
            long connectionid = R.NextInt64();
            lock (_connectionIdLock)
            {
                ConnectionIds.Add(new ConnectionId(DateTime.Now + TimeSpan.FromMinutes(2), connectionid));
            }
            Console.WriteLine("added new connectionid "+connectionid);
            byte[] response = new byte[16];
            0.GetBigendianBytes().CopyTo(response,0);
            transactionid.GetBigendianBytes().CopyTo(response, 4);
            connectionid.GetBigendianBytes().CopyTo(response, 8);
            return response;
        }
        
        private byte[] HandleAnnounce(byte[] request, IPEndPoint host)
        {
            long connectionid = request.DecodeLong(0);
            int transactionid = request.DecodeInt(12);
            string info_hash = BitConverter.ToString(request, 16, 20);
            string peer_id= request.DecodeString(36, 20);
            short port = request.DecodeShort(96);
            long left = request.DecodeLong(64);
            int num_want = request.DecodeInt(92);
            if (num_want > 0) num_want = 50;
            IPAddress hostipaddress = host.Address;

            
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
                return HandleError(request,"No valid IPv6 address.");
            }
            Peer[] allpeers;
            lock (_peerLock)
            {
                Peer? currentpeer = Peers.SingleOrDefault(x => (x.LocalAddress.Equals(localipaddress) || x.Address.Equals(publicipaddress)) && x.InfoHash == info_hash && x.Port == port);
                if (currentpeer == null)
                {
                    Peer newPeer = new Peer(publicipaddress, port, info_hash, localipaddress);
                    if (left > 0) newPeer.Status = Peer.PeersStatus.Leech;
                    else newPeer.Status = Peer.PeersStatus.Seed;

                    Peers.Add(newPeer);


                    Console.WriteLine("Added peer: " + publicipaddress.ToString() + ":" + port);
                }
                else
                {
                    currentpeer.Refresh();
                }

                allpeers = Peers.Where(x => x.AddressFamily == host.AddressFamily && x.InfoHash == info_hash).ToArray();
            }
            
            var peerstosend = allpeers.Take(num_want).ToArray();


            int peerdatalen = host.AddressFamily == AddressFamily.InterNetwork ? 6 : 18;
            byte[] response = new byte[20+ peerstosend.Length*peerdatalen];
            1.GetBigendianBytes().CopyTo(response,0);
            transactionid.GetBigendianBytes().CopyTo(response,4);
            ((int)Math.Round(AnnounceInterval.TotalSeconds)).GetBigendianBytes().CopyTo(response, 8);
            Peers.Where(x=>x.Status==Peer.PeersStatus.Leech).Count().GetBigendianBytes().CopyTo(response, 12);
            Peers.Where(x => x.Status == Peer.PeersStatus.Seed).Count().GetBigendianBytes().CopyTo(response, 16);

            for (int i = 0; i < peerstosend.Length; i++)
            {
                if (localipaddress==null|| peerstosend[i].LocalAddress==null)
                {
                    peerstosend[i].Address.GetAddressBytes().CopyTo(response, 20 + (i * peerdatalen));
                }
                else
                {
                    peerstosend[i].LocalAddress.GetAddressBytes().CopyTo(response,20+ (i * peerdatalen));
                }
                peerstosend[i].Port.GetBigendianBytes().CopyTo(response, 20+peerdatalen-2 + (i * peerdatalen));

            }
            Console.WriteLine("Responded to client.");
            return response;
        }

        private byte[] HandleError(byte[] request, string message)
        {
            int transactionid = request.DecodeInt(12);
            byte[] msg = Encoding.UTF8.GetBytes(message);
            byte[] response = new byte[8+msg.Length];
            BitConverter.GetBytes(3).CopyTo(response, 0);
            BitConverter.GetBytes(transactionid).CopyTo(response, 0);
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

        

        private record struct ConnectionId(DateTime exp,long id);
    }
}
