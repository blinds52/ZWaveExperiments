using System;

namespace ZwaveExperiments.SerialProtocol.LowLevel
{
    static class SerialCheckSum
    {
        public static byte Compute(ReadOnlySpan<byte> data)
        {
            var length = data.Length;
            var result = 0;
            for (int i = 0; i < length; i++)
            {
                result ^= data[i];
            }

            return (byte) (~result);
        }
    }
}