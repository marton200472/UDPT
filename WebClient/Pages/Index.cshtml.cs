using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Shared;
using Shared.Models;

namespace WebClient.Pages
{
    public class IndexModel : PageModel
    {
        private readonly ILogger<IndexModel> _logger;
        private readonly TrackerDbContext _trackerDbContext;

        public List<Torrent> Torrents=new List<Torrent>();
        public List<Peer> Peers=new List<Peer>();

        public IndexModel(ILogger<IndexModel> logger, TrackerDbContext dbContext)
        {
            _logger = logger;
            _trackerDbContext = dbContext;
        }



        public void OnGet()
        {
            Torrents = _trackerDbContext.Torrents.ToList();
            Peers=_trackerDbContext.Peers.ToList();
        }
    }
}