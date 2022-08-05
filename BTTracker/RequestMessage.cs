using System.Net.Sockets;
using System.Net;

namespace BTTracker
{
    public record struct RequestMessage(UdpClient client, IPEndPoint remoteEP, byte[] data, DateTime received);
}
