using BTTracker;
using System.Net;
using System.Net.Sockets;

TrackerConfig config = TrackerConfig.FromFile("tracker.cnfg");
UdpTracker tracker = new UdpTracker(config, TimeSpan.FromMinutes(30));
tracker.Start();
Console.WriteLine("Press any key to exit...");
Console.ReadKey();
Console.WriteLine("Exiting...");
tracker.Stop();
