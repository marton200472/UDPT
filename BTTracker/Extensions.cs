using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace BTTracker
{
    public static class Extensions
    {

        public static int DecodeInt(this byte[] source, int offset=0)
        {
            byte[] correct = Enumerable.Range(offset, 4).Select(x => source[x]).ToArray();
            if (BitConverter.IsLittleEndian)
                Array.Reverse(correct);

            int i = BitConverter.ToInt32(correct);
            return i;
        }

        public static short DecodeShort(this byte[] source, int offset = 0)
        {
            byte[] correct = Enumerable.Range(offset, 2).Select(x => source[x]).ToArray();
            if (BitConverter.IsLittleEndian)
                Array.Reverse(correct);

            short i = BitConverter.ToInt16(correct);
            return i;
        }

        public static long DecodeLong(this byte[] source, int offset=0)
        {
            byte[] correct = Enumerable.Range(offset, 8).Select(x => source[x]).ToArray();

            long i = BitConverter.ToInt64(correct);
            return i;
        }

        public static string DecodeString(this byte[] source, int offset, int length)
        {
            byte[] correct = Enumerable.Range(offset, length).Select(x => source[x]).ToArray();
            if (BitConverter.IsLittleEndian)
                Array.Reverse(correct);
            return Encoding.UTF8.GetString(correct);
        }

        public static byte[] GetBigendianBytes(this int a)
        {
            byte[] converted = BitConverter.GetBytes(a);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(converted);
            }
            return converted;
        }
        public static byte[] GetBigendianBytes(this long a)
        {
            byte[] converted = BitConverter.GetBytes(a);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(converted);
            }
            return converted;
        }

        public static byte[] GetBigendianBytes(this short a)
        {
            byte[] converted = BitConverter.GetBytes(a);
            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(converted);
            }
            return converted;
        }
        public static byte[] GetBigendianBytes(this string a)
        {
            byte[] converted = Encoding.UTF8.GetBytes(a);
            return converted;
        }

        public static bool IsInSubnet(this IPAddress address, string subnetMask)
        {
            var slashIdx = subnetMask.IndexOf("/");
            if (slashIdx == -1)
            { // We only handle netmasks in format "IP/PrefixLength".
                throw new NotSupportedException("Only SubNetMasks with a given prefix length are supported.");
            }

            // First parse the address of the netmask before the prefix length.
            var maskAddress = IPAddress.Parse(subnetMask.Substring(0, slashIdx));

            if (maskAddress.AddressFamily != address.AddressFamily)
            { // We got something like an IPV4-Address for an IPv6-Mask. This is not valid.
                return false;
            }

            // Now find out how long the prefix is.
            int maskLength = int.Parse(subnetMask.Substring(slashIdx + 1));

            if (maskLength == 0)
            {
                return true;
            }

            if (maskLength < 0)
            {
                throw new NotSupportedException("A Subnetmask should not be less than 0.");
            }

            if (maskAddress.AddressFamily == AddressFamily.InterNetwork)
            {
                // Convert the mask address to an unsigned integer.
                var maskAddressBits = BitConverter.ToUInt32(maskAddress.GetAddressBytes().Reverse().ToArray(), 0);

                // And convert the IpAddress to an unsigned integer.
                var ipAddressBits = BitConverter.ToUInt32(address.GetAddressBytes().Reverse().ToArray(), 0);

                // Get the mask/network address as unsigned integer.
                uint mask = uint.MaxValue << (32 - maskLength);

                // https://stackoverflow.com/a/1499284/3085985
                // Bitwise AND mask and MaskAddress, this should be the same as mask and IpAddress
                // as the end of the mask is 0000 which leads to both addresses to end with 0000
                // and to start with the prefix.
                return (maskAddressBits & mask) == (ipAddressBits & mask);
            }

            if (maskAddress.AddressFamily == AddressFamily.InterNetworkV6)
            {
                // Convert the mask address to a BitArray. Reverse the BitArray to compare the bits of each byte in the right order.
                var maskAddressBits = new BitArray(maskAddress.GetAddressBytes().Reverse().ToArray());

                // And convert the IpAddress to a BitArray. Reverse the BitArray to compare the bits of each byte in the right order.
                var ipAddressBits = new BitArray(address.GetAddressBytes().Reverse().ToArray());
                var ipAddressLength = ipAddressBits.Length;

                if (maskAddressBits.Length != ipAddressBits.Length)
                {
                    throw new ArgumentException("Length of IP Address and Subnet Mask do not match.");
                }

                // Compare the prefix bits.
                for (var i = ipAddressLength - 1; i >= ipAddressLength - maskLength; i--)
                {
                    if (ipAddressBits[i] != maskAddressBits[i])
                    {
                        return false;
                    }
                }

                return true;
            }

            throw new NotSupportedException("Only InterNetworkV6 or InterNetwork address families are supported.");
        }
    }
}
