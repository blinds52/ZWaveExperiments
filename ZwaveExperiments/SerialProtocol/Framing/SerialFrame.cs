using System;
using System.Buffers;
using System.Diagnostics;

namespace ZwaveExperiments.SerialProtocol.Framing
{
    internal struct SerialFrame
    {
        public static SerialFrame Ack = new SerialFrame(SerialFrameHeader.ACK, true);
        public static SerialFrame Nak = new SerialFrame(SerialFrameHeader.NAK, true);

        public SerialFrameHeader Header { get; }
        public byte[] Data { get; }
        public int Length => Data.Length == 0 ? 1 : Data.Length;

        private SerialFrame(SerialFrameHeader header, bool allocate)
        {
            Header = header;
            Data = new byte[] { (byte)header };
        }

        public SerialFrame(SerialFrameHeader header)
        {
            Header = header;
            Data = new byte[0];
        }

        public SerialFrame(byte[] data)
        {
            Debug.Assert(data[0] == (byte)SerialFrameHeader.SOF);
            Header = SerialFrameHeader.SOF;
            Data = data;
        }

        public SerialDataFrame AsSerialDataFrame()
        {
            if (Header != SerialFrameHeader.SOF || Data == null)
            {
                throw new InvalidOperationException("Not a data frame");
            }

            var result = new SerialDataFrame(Data);
            return result;
        }
    }
}