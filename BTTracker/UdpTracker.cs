using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Net;
using Microsoft.EntityFrameworkCore;

namespace BTTracker
{
    internal class UdpTracker
    {
        private readonly UdpClient client;
        private List<ConnectionId> ConnectionIds = new List<ConnectionId>();
        private List<Peer> Peers = new List<Peer>();
        private object ConnectionIdLock = new();
        private object ClientLock=new();
        private object DbLock = new();
        private CancellationTokenSource ListenerTokenSource = new CancellationTokenSource();
        private static Random R = new Random();
        private System.Timers.Timer DeleteTimer;
        private TrackerDbContext DbContext;

        private IPAddress HostAddress;
        private int HostPort;

        private DbContextOptions DbOptions;

        private TimeSpan AnnounceInterval;

        public UdpTracker(IPAddress address, int port,TimeSpan announceInterval,DbContextOptions dboptions =null)
        {
            HostAddress = address;
            HostPort = port;
            AnnounceInterval = announceInterval;
            if (dboptions==null)
            {
                string connstr = @$"Data Source={new System.IO.FileInfo(System.Reflection.Assembly.GetExecutingAssembly().Location).DirectoryName}/tracker.db;".Replace('\\','/');
                dboptions = new DbContextOptionsBuilder()
                    .UseSqlite(connstr).Options;
                
            }
            DbOptions = dboptions;
            DbContext = new TrackerDbContext(dboptions);
            DbContext.Database.Migrate();
            client = new UdpClient();
            client.Client.Bind(new IPEndPoint(address,port));
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
            Task.Run(() => Listen(ListenerTokenSource.Token));
            DeleteTimer.Start();
            Console.WriteLine($"UDP Tracker running on {HostAddress} port {HostPort}");
        }

        public void Stop()
        {
            ListenerTokenSource.Cancel();
            DeleteTimer.Stop();
            System.Threading.Thread.Sleep(1000);
        }

        private enum Action
        {
            Connect=0, Announce=1, Scrape=2, Error=3
        }

        private void DeleteExpiredIds()
        {
            int deletedids = 0;
            lock(ConnectionIdLock) {
                for (int i = 0; i < ConnectionIds.Count; i++)
                {
                    if (ConnectionIds[i].exp<DateTime.Now)
                    {
                        ConnectionIds.RemoveAt(i);
                        deletedids++;
                        i--;
                    }
                }
            }
            Console.WriteLine(DateTime.Now+"\t[Expired ID Deleter]: Deleted "+deletedids+" connectionids.");
            
        }

        private void DeleteExpiredPeers()
        {
            using (TrackerDbContext ctx=new TrackerDbContext(DbOptions))
            {
                Peer[] peerstodelete = ctx.Peers.Where(x => x.TimeStamp < DateTime.Now - AnnounceInterval).ToArray();
                ctx.Peers.RemoveRange(peerstodelete);
                ctx.SaveChanges();
            }
        }

        private async Task Listen(CancellationToken token)
        {
            while (true)
            {
                var packet = await client.ReceiveAsync(token);
                if (token.IsCancellationRequested) return;
                _=Task.Run(() => HandleRequest(packet));
            }
        }

        private void HandleRequest(UdpReceiveResult request)
        {
            byte[] requestdata = request.Buffer;
            Console.WriteLine((0x41727101980 == BitConverter.ToInt64(requestdata, 0)));
            if (requestdata.Length < 16)
            {
                return;
            }
            IPEndPoint host = request.RemoteEndPoint;

            Action action = GetAction(requestdata);
            byte[] response = null;
            switch (action)
            {
                case Action.Connect:
                    response = HandleConnect(requestdata);
                    break;
                case Action.Announce:
                    response = HandleAnnounce(requestdata, host);
                    break;
                case Action.Scrape:
                    response = HandleError(requestdata);
                    break;
                default:
                    response = HandleError(requestdata);
                    break;
            }
            lock (ClientLock)
            {
                client.Send(response, response.Length, host);
            }
            
        }

        private byte[] HandleConnect(byte[] request)
        {
            int transactionid = request.DecodeInt(12);
            long connectionid = R.NextInt64();
            lock (ConnectionIdLock)
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
            int transactionid = request.DecodeInt(12);
            string info_hash = request.DecodeString(16, 20);
            string peer_id= request.DecodeString(36, 20);
            short port = request.DecodeShort(96);
            byte[] ipaddress = host.Address.GetAddressBytes();
            Peer[] peers=new Peer[0];
            using (TrackerDbContext ctx = new TrackerDbContext(DbOptions))
            {
                try
                {
                    peers = ctx.Peers.Where(x => x.InfoHash == info_hash).ToArray();
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);

                }

                Peer currentpeer = ctx.Peers.ToArray().SingleOrDefault(x =>
                 {
                     for (int i = 0; i < 4; i++)
                     {
                         if (x.Address[i] != ipaddress[i]) return false;
                     }
                     if (x.InfoHash != info_hash) return false;
                     if (x.Port != port) return false;
                     return true;
                 });
                if (currentpeer==null)
                {
                    ctx.Peers.Add(new Peer(ipaddress, port, info_hash));
                }
                else
                {
                    currentpeer.Refresh();
                }
                
                
                ctx.SaveChanges();
            }
            byte[] response = new byte[20+peers.Length*6];
            1.GetBigendianBytes().CopyTo(response,0);
            transactionid.GetBigendianBytes().CopyTo(response,4);
            30.GetBigendianBytes().CopyTo(response, 8);
            0.GetBigendianBytes().CopyTo(response, 12);
            peers.Length.GetBigendianBytes().CopyTo(response, 16);

            for (int i = 0; i < peers.Length; i++)
            {
                peers[i].Address.CopyTo(response, 20 + (i * 6));
                peers[i].Port.GetBigendianBytes().CopyTo(response, 24 + (i * 6));
            }
            return response;
        }

        private byte[] HandleError(byte[] request)
        {
            int transactionid = request.DecodeInt(12);
            byte[] message = Encoding.UTF8.GetBytes("error occured");
            byte[] response = new byte[8+message.Length];
            BitConverter.GetBytes(3).CopyTo(response, 0);
            BitConverter.GetBytes(transactionid).CopyTo(response, 0);
            message.CopyTo(response, 8);
            return response;
        }

        private Action GetAction(byte[] request)
        {
            int i = request.DecodeInt(8);
            return (Action)i;
        }

        

        private record struct ConnectionId(DateTime exp,long id);
    }
}
