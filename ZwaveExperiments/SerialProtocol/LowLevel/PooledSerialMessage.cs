using System;
using System.Buffers;
using System.Diagnostics;

namespace ZwaveExperiments.SerialProtocol.LowLevel
{
    internal struct SerialFrame
    {
        public static SerialFrame Ack = new SerialFrame(FrameHeader.ACK, true);
        public static SerialFrame Nak = new SerialFrame(FrameHeader.NAK, true);

        public FrameHeader Header { get; }
        public byte[] Data { get; }
        public int Length => Data.Length == 0 ? 1 : Data.Length;

        private SerialFrame(FrameHeader header, bool allocate)
        {
            Header = header;
            Data = new byte[] { (byte)header };
        }

        public SerialFrame(FrameHeader header)
        {
            Header = header;
            Data = new byte[0];
        }

        public SerialFrame(byte[] data)
        {
            Debug.Assert(data[0] == (byte)ZwaveExperiments.FrameHeader.SOF);
            Header = FrameHeader.SOF;
            Data = data;
        }

        public SerialDataFrame AsSerialDataFrame()
        {
            if (Header != FrameHeader.SOF || Data == null)
            {
                throw new InvalidOperationException("Not a data frame");
            }

            var result = new SerialDataFrame(Data);
            return result;
        }
    }

    struct SerialDataFrame
    {
        const byte HEADER = (byte)FrameHeader.SOF;

        public FrameType Type
        {
            get => (FrameType) Data[2];
            private set => Data[2] = (byte) value;
        }

        public SerialCommand Command
        {
            get => (SerialCommand) Data[3];
            private set => Data[3] = (byte) value;
        }

        public Span<byte> Parameters => Data.AsSpan().Slice(2, Data.Length - 3);

        public byte CheckSum
        {
            get => Data[Data.Length - 1];
            private set => Data[Data.Length - 1] = value;
        }

        public byte InfoSize
        {
            get => Data[1];
            private set => Data[1] = value;
        }

        public byte[] Data { get; private set; }

        public SerialDataFrame(FrameType type, SerialCommand command, int parametersSize = 0)
        {
            if (parametersSize > 256)
            {
                throw new ArgumentOutOfRangeException("Parameters are too big");
            }

            var dataSize = parametersSize + 5;
            Data = new byte[dataSize];
            Data[0] = (byte)FrameHeader.SOF;
            InfoSize = (byte) (parametersSize + 3);
            Type = type;
            Command = command;
            AssertFrame();
        }

        public SerialDataFrame(byte[] bytes)
        {
            Data = bytes;
        }

        public SerialFrame AsSerialFrame()
        {
            return new SerialFrame(Data);
        }

        [Conditional("DEBUG")]
        private void AssertFrame()
        {
            Debug.Assert(!IsTooSmall, $"Invalid frame, length = {Data.Length}");
            Debug.Assert(!IsTooBig, $"Frame is too big, length = {Data.Length}");
            Debug.Assert(IsHeaderValid, $"Not a data frame, [0] = 0x{Data[0]:X2}");
            Debug.Assert(IsLengthValid, $"Invalid length {Data[1]} expected {Data.Length - 2}");
        }

        public byte ComputeCheckSum()
        {
            var checksumable = Data.AsSpan().Slice(1, Data.Length - 2);
            return SerialCheckSum.Compute(checksumable);
        }

        public bool IsHeaderValid => Data[0] == HEADER;
        public bool IsCheckSumValid => ComputeCheckSum() == CheckSum;
        public bool IsTooSmall => Data.Length < 5;
        public bool IsTooBig => Data.Length > 256 + 2;
        public bool IsLengthValid => InfoSize == Data.Length - 2;

        public bool IsValid
            => !IsTooSmall && !IsTooBig && IsHeaderValid && IsLengthValid && IsCheckSumValid;

        public void UpdateCheckSum()
        {
            CheckSum = ComputeCheckSum();
        }
    }
}