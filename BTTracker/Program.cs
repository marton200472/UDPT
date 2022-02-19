using BTTracker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

IHost host=Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        services.AddSingleton(TrackerConfig.FromFile("tracker.cnfg"));
        services.AddDbContext<TrackerDbContext>(cnfg => {
            string connstr = @$"Data Source={new System.IO.FileInfo(System.Reflection.Assembly.GetExecutingAssembly().Location).DirectoryName}/tracker.db;".Replace('\\', '/');
            
                cnfg.UseSqlite(connstr);
        });
        services.AddHostedService<UdpTracker>();
    })
    .UseSystemd()
    .Build();
await host.RunAsync();

