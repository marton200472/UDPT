using Shared.Models;

namespace WebInterface.Models
{
    public class TrackerDataViewModel
    {
        public TrackerDataViewModel(List<Torrent> torrents, List<Peer> peers)
        {
            Torrents = torrents;
            Peers = peers;
        }

        public List<Torrent> Torrents { get; set; }
        public List<Peer> Peers { get; set; }
    }
}
