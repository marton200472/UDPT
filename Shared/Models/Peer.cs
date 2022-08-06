using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Shared.Models
{
    [Table("Peers")]
    public class Peer
    {
        public Peer(string peer_id, IPAddress address, ushort port, string infohash, IPAddress? localAddress = null)
        {
            Address = address;
            Port = port;
            InfoHash = infohash;
            LocalAddress = localAddress;
            PeerId = peer_id;
            Refresh();
        }

        internal Peer() { }


        public void Refresh()
        {
            TimeStamp = DateTime.UtcNow;
        }
        
        [Key]
        public uint Id { get; set; }
        [Column(TypeName = "varchar(40)")]
        public string PeerId { get; set; }
        public IPAddress Address { get; set; }
        public IPAddress? LocalAddress { get; set; }
        public System.Net.Sockets.AddressFamily AddressFamily => Address.AddressFamily;
        public ushort Port { get; set; }
        [Column(TypeName = "varchar(40)")]
        public string InfoHash { get; set; }
        public DateTime TimeStamp { get; set; }
        public PeersStatus Status { get; set; }

        public enum PeersStatus
        {
            Seed, Leech
        }
    }
}
