using BTTracker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

IHost host=Host.CreateDefaultBuilder(args)
    .ConfigureDefaults(args)
    .ConfigureServices(services =>
    {
        var config = TrackerConfig.FromFile("tracker.cnfg");
        services.AddSingleton(config);
        services.AddDbContext<Shared.TrackerDbContext>(cnfg => {
            cnfg.UseMySql(config.ConnectionString, ServerVersion.AutoDetect(config.ConnectionString));
            
            
        });
        services.AddHostedService<UdpTracker>();
    })
    .ConfigureLogging((context, logging) => {
        
        // ...
        logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
        logging.AddFilter("Microsoft.EntityFrameworkCore.Infrastructure",LogLevel.Warning);
    })
    .UseSystemd()
    .Build();
await host.RunAsync();

