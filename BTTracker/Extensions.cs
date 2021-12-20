using System;
using System.Collections.Generic;
using System.Linq;
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
    }
}
