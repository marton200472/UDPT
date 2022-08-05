using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shared.Models
{
    [Table("Torrents")]
    public class Torrent
    {
        public Torrent(string info_hash)
        {
            InfoHash = info_hash;
        }

        internal Torrent()
        {

        }

        [Key]
        [Required]
        public string InfoHash { get; set; }
        public int Seeders { get; set; } = 0;
        public int Leechers { get; set; } = 0;
        public int Downloaded { get; set; } = 0;
    }
}
