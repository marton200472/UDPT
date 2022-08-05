using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
var connStr=builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<Shared.TrackerDbContext>(ctx =>
{
    ctx.UseMySql(connStr,ServerVersion.AutoDetect(connStr));
});

var app = builder.Build();


app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapRazorPages();

app.Run();