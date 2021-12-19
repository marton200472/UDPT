using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Net;

namespace BTTracker
{
    internal class UdpTracker
    {
        private readonly UdpClient client;
        private List<ConnectionId> ConnectionIds = new List<ConnectionId>();
        private object ConnectionIdLock = new();
        private object ClientLock=new();
        private CancellationTokenSource ListenerTokenSource = new CancellationTokenSource();
        private static Random R = new Random();
        private System.Timers.Timer DeleteTimer;

        public UdpTracker(IPAddress address, int port)
        {
            client = new UdpClient();
            client.Client.Bind(new IPEndPoint(address,port));
            DeleteTimer = new System.Timers.Timer(30000) { AutoReset = true };
            DeleteTimer.Elapsed += DeleteTimer_Elapsed;
        }

        private void DeleteTimer_Elapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            DeleteExpiredIds();
        }

        public void Start()
        {
            Task.Run(() => Listen(ListenerTokenSource.Token));
            DeleteTimer.Start();
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
                    response = HandleError(requestdata);
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
            int transactionid = request.ToInt(12);
            long connectionid = R.NextInt64();
            lock (ConnectionIdLock)
            {
                ConnectionIds.Add(new ConnectionId(DateTime.Now + TimeSpan.FromMinutes(2), connectionid));
            }
            Console.WriteLine("added new connectionid "+connectionid);
            byte[] response = new byte[16];
            BitConverter.GetBytes(0).CopyTo(response,0);
            BitConverter.GetBytes(transactionid).CopyTo(response, 4);
            BitConverter.GetBytes(connectionid).CopyTo(response, 8);
            return response;
        }
        
        private byte[] HandleAnnounce(byte[] request, IPEndPoint host)
        {
            int transactionid = request.ToInt(12);
            string info_hash = request.DecodeString(16, 20);
            string peer_id= request.DecodeString(36, 20);
            int port = request.ToInt(96);
            IPAddress ipaddress = host.Address;

        }

        private byte[] HandleError(byte[] request)
        {
            int transactionid = request.ToInt(12);
            byte[] message = Encoding.UTF8.GetBytes("error occured");
            byte[] response = new byte[8+message.Length];
            BitConverter.GetBytes(3).CopyTo(response, 0);
            BitConverter.GetBytes(transactionid).CopyTo(response, 0);
            message.CopyTo(response, 8);
            return response;
        }

        private Action GetAction(byte[] request)
        {
            int i = request.ToInt(8);
            return (Action)i;
        }

        

        private record struct ConnectionId(DateTime exp,long id);
    }
}
