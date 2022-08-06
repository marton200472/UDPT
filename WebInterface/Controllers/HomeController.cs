using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using WebInterface.Models;
using Shared;
using Shared.Models;

namespace WebInterface.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly TrackerDbContext _trackerDbContext;

        public HomeController(ILogger<HomeController> logger, TrackerDbContext dbContext)
        {
            _logger = logger;
            _trackerDbContext = dbContext;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        public IActionResult TrackerData()
        {
            return PartialView(new TrackerDataViewModel(_trackerDbContext.Torrents.ToList(), _trackerDbContext.Peers.ToList()));
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}