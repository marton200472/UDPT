using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTTracker.UDPMessages
{
	internal class AnnounceRequest
	{
		internal const int Action = 1;
		internal System.Net.Sockets.AddressFamily AddressFamily { get; }

		internal long ConnectionId { get; }
		internal int TransactionId { get; }
		internal string InfoHash { get; }
		internal string PeerId { get; }
		internal long Downloaded { get; }
		internal long Left { get; }
		internal long Uploaded { get; }
		internal AnnounceEvent Event { get; }
		//IPAddress and Key ignored
		internal int WantedClients { get; }
		internal ushort Port { get; set; }

		private AnnounceRequest(byte[] source, System.Net.Sockets.AddressFamily addressFamily)
		{
			AddressFamily = addressFamily;

			ConnectionId = source.DecodeLong(0);
			TransactionId = source.DecodeInt(12);
			InfoHash = source.DecodeInfoHash(16, 20);
            PeerId = source.DecodeString(36, 20);
			Downloaded = source.DecodeLong(56);
			Left = source.DecodeLong(64);
			Uploaded = source.DecodeLong(72);
			Event = (AnnounceEvent)source.DecodeInt(80);
			WantedClients = Math.Min(source.DecodeInt(92),50);
			Port = source.DecodeUShort(96);
		}

		internal byte[] GetResponseBytes(TimeSpan announceInterval,int leechers, int seeders,IEnumerable<Peer> peers)
		{
			int addresslen = AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? 4 : 16;
			byte[] response = new byte[20+peers.Count()*(addresslen+2)];
			Action.GetBigendianBytes().CopyTo(response, 0);
			TransactionId.GetBigendianBytes().CopyTo(response, 4);
			((int)Math.Round(announceInterval.TotalSeconds)).GetBigendianBytes().CopyTo(response, 8);
			leechers.GetBigendianBytes().CopyTo(response, 12);
			seeders.GetBigendianBytes().CopyTo(response, 16);
			int offset = 20;
			
			foreach (var peer in peers)
			{
				peer.Address.GetAddressBytes().CopyTo(response, offset);
				peer.Port.GetBigendianBytes().CopyTo(response, offset + addresslen);
				offset += addresslen+2;
			}   
			return response;
		}

		internal static AnnounceRequest FromByteArray(byte[] bytes, System.Net.Sockets.AddressFamily addressFamily)
		{
			return new AnnounceRequest(bytes, addressFamily);
		}

		internal enum AnnounceEvent
		{
			None, Completed, Started, Stopped
		}
	}
}
