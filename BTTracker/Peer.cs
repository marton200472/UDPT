using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace BTTracker
{
    [Table("Peers")]
    public class Peer
    {
        public Peer(IPAddress address, short port, string infohash, IPAddress? localAddress=null)
        {
            Address = address;
            Port = port;
            InfoHash = infohash;
            LocalAddress = localAddress;
            Refresh();
        }


        internal void Refresh()
        {
            TimeStamp = DateTime.Now;
        }

        [Key]
        public int Id { get; set; }
        public IPAddress Address { get; set; }
        public IPAddress? LocalAddress { get; set; }
        public System.Net.Sockets.AddressFamily AddressFamily => Address.AddressFamily;
        public short Port { get; set; }
        public string InfoHash { get; set; }
        public DateTime TimeStamp { get; set; }
        public PeersStatus Status { get; set; }

        public enum PeersStatus
        {
            Seed, Leech
        }
    }
}
