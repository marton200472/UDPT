using BTTracker;
using System.Net;

UdpTracker tracker = new UdpTracker(IPAddress.Any, 55555, TimeSpan.FromMinutes(30));
tracker.Start();
Console.WriteLine("Tracker started. Press any key to continue.");
Console.ReadKey();
Console.WriteLine("Exiting...");
tracker.Stop();
