using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTTracker
{
    public static class Extensions
    {

        public static int ToInt(this byte[] source, int offset=0)
        {
            byte[] correct = Enumerable.Range(offset, 4).Select(x => source[x]).ToArray();
            if (BitConverter.IsLittleEndian)
                Array.Reverse(correct);

            int i = BitConverter.ToInt32(correct);
            return i;
        }

        public static long ToLong(this byte[] source, int offset=0)
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
    }
}
