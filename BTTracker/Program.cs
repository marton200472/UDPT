using BTTracker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

IHost host=Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddSingleton(TrackerConfig.FromFile("tracker.cnfg"));

        services.AddHostedService<UdpTracker>();
    })
    .UseSystemd()
    .Build();
await host.RunAsync();

