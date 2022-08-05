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
        private ConcurrentQueue<string> TorrentsToUpdate = new();


        private IPAddress PublicIPv4Address;
        private ConcurrentQueue<RequestMessage> Requests = new ConcurrentQueue<RequestMessage>();
        private ConcurrentQueue<ResponseMessage> Responses = new ConcurrentQueue<ResponseMessage>();
        private IServiceProvider ServiceProvider;
        private TrackerConfig TrackerConfig;
        private List<DataHandlerThread> HandlerThreads = new List<DataHandlerThread>();


        private bool keepRunning;
        private bool IsRunning;

        public UdpTracker(TrackerConfig config,IServiceProvider serviceProvider, ILogger<UdpTracker> logger)
        {
            AnnounceInterval = config.AnnounceInterval;
            TrackerConfig = config;
            ServiceProvider = serviceProvider;
            if (config==null) config=TrackerConfig.Default;


            _logger = logger;

        }

        

        public Task StartAsync(CancellationToken token)
        {
            PublicIPv4Address = GetPublicIPv4Address();
            _logger.LogInformation("Public IP address: {0}", PublicIPv4Address);
            _logger.LogInformation("Starting handlers...");

            for (int i = 0; i < 4; i++)
            {
                var handler = new DataHandlerThread(ServiceProvider, Requests, Responses, ConnectionIds,TorrentsToUpdate, new IPAddress[] { GetLocalIPv4Address() }, new IPAddress[] { PublicIPv4Address }, AnnounceInterval);
                handler.Start();
                HandlerThreads.Add(handler);
            }

            int clientid = 0;
            foreach (var endpoint in TrackerConfig.Endpoints)
            {
                var client = new UdpClient(endpoint);
                client.Client.Blocking = false;
                client.BeginReceive(OnMessageReceived, client);
                
                _logger.LogInformation("UDP Tracker running on {0} port {1}", endpoint.Address, endpoint.Port);
                clientid++;
            }

            keepRunning = true;
            new Thread(SendResponses).Start();
            new Thread(DeleteExpiredItems).Start();
            new Thread(TorrentInfoUpdater).Start();

            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken token)
        {


            while (!Requests.IsEmpty)
            {
                await Task.Delay(100);
            }
            foreach (var handler in HandlerThreads)
            {
                handler.Stop();
            }
            while (HandlerThreads.Any(x=>x.IsRunning))
            {
                await Task.Delay(100);
            }
            keepRunning=false;
            while (IsRunning)
            {
                await Task.Delay(100);
            }


        }

        private void SendResponses()
        {
            IsRunning = true;
            while (!Responses.IsEmpty||keepRunning)
            {
                if (Responses.TryDequeue(out var response))
                {
                    var client = response.client;
                    if (client != null)
                    {
                        client.Send(response.payload, response.target);
                    }
                }
                else
                {
                    Thread.Sleep(10);
                }
            }
            IsRunning = false;
        }

        private void TorrentInfoUpdater(){
            var context=ServiceProvider.CreateScope().ServiceProvider.GetService<TrackerDbContext>()!;
            while (keepRunning)
            {
                if (TorrentsToUpdate.TryDequeue(out var hash))
                {
                    Torrent? torrent=context.Torrents.Find(hash);
                    if (torrent is null)
                    {
                        torrent=new Torrent(hash);
                        context.Torrents.Add(torrent);
                    }
                    torrent.Seeders=context.Peers.Count(x=>x.InfoHash==hash && x.Status==Peer.PeersStatus.Seed);
                    torrent.Leechers=context.Peers.Count(x=>x.InfoHash==hash && x.Status==Peer.PeersStatus.Leech);
                    context.SaveChanges();

                }
                else
                {
                    Thread.Sleep(5000);
                }
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

        private void DeleteExpiredItems(){
            var context = ServiceProvider.CreateScope().ServiceProvider.GetService<TrackerDbContext>()!;
            while (keepRunning)
            {
                DeleteExpiredIds();
                var peerstoremove = context.Peers.Where(x => (x.TimeStamp + AnnounceInterval) < DateTime.Now);
                context.Peers.RemoveRange(peerstoremove.ToArray());
                context.SaveChanges();
                Thread.Sleep(10000);
            }
                
            context.Dispose();
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

        

        

        

        

        

        private void OnMessageReceived(IAsyncResult result)
        {
            var client = (UdpClient)result.AsyncState!;
            IPEndPoint? ep = null;
            byte[] data = client.EndReceive(result, ref ep);

            Requests.Enqueue(new RequestMessage(client,ep!,data,DateTime.Now));
            client.BeginReceive(OnMessageReceived, client);
        }
        
        
    }
}
