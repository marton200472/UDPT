using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTTracker.UDPMessages
{
    internal class ConnectionRequest
    {
        internal const int Action = 0;
        internal int TransactionId { get; }
        private ConnectionRequest(byte[] source)
        {
            TransactionId = source.DecodeInt(12);
        }

        internal byte[] GetResponseBytes(long connectionId)
        {
            byte[] response = new byte[16];
            Action.GetBigendianBytes().CopyTo(response, 0);
            TransactionId.GetBigendianBytes().CopyTo(response, 4);
            connectionId.GetBigendianBytes().CopyTo(response, 8);
            return response;
        }

        internal static ConnectionRequest FromByteArray(byte[] bytes)
        {
            return new ConnectionRequest(bytes);
        }
    }
}
