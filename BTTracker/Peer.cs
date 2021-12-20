using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTTracker
{
    [Table("Peers")]
    public class Peer
    {
        public Peer(byte[] address, short port, string infohash)
        {
            Address = address;
            Port = port;
            InfoHash = infohash;
        }

        public Peer()
        {

        }

        internal void Refresh()
        {
            TimeStamp = DateTime.Now;
        }

        [Key]
        public int Id { get; set; }
        public byte[] Address { get; set; }
        public short Port { get; set; }
        public string InfoHash { get; set; }
        public DateTime TimeStamp { get; set; }
    }
}
