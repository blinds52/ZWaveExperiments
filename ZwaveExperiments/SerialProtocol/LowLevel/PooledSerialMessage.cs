using System;
using System.Buffers;
using System.Diagnostics;

namespace ZwaveExperiments.SerialProtocol.LowLevel
{
    struct PooledSerialFrame : IDisposable
    {
        public static PooledSerialFrame Ack = new PooledSerialFrame(FrameHeader.ACK, true);
        public static PooledSerialFrame Nak = new PooledSerialFrame(FrameHeader.NAK, true);

        public FrameHeader Header { get; }
        public ArrayPool<byte> Pool { get; }
        public ArraySegment<byte> Data { get; }
        public int Length => Data.Count == 0 ? 1 : Data.Count;

        private PooledSerialFrame(FrameHeader header, bool allocate)
        {
            Header = header;
            Pool = null;
            Data = new byte[] { (byte)header };
        }

        public PooledSerialFrame(FrameHeader header)
        {
            Header = header;
            Pool = null;
            Data = ArraySegment<byte>.Empty;
        }

        public PooledSerialFrame(ArrayPool<byte> pool, ArraySegment<byte> data)
        {
            Debug.Assert(data[0] == (byte)ZwaveExperiments.FrameHeader.SOF);
            Header = FrameHeader.SOF;
            Pool = pool;
            Data = data;
        }

        public void Dispose()
        {
            Pool?.Return(Data.Array);
        }
    }

    ref struct DataFrame
    {
        const byte HEADER = (byte)FrameHeader.SOF;

        public FrameType Type
        {
            get => (FrameType) Data[2];
            private set => Data[2] = (byte) value;
        }

        public Command Command
        {
            get => (Command) Data[3];
            private set => Data[3] = (byte) value;
        }

        public Span<byte> Parameters => Data.Slice(2, Data.Length - 3);

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

        public Span<byte> Data { get; private set; }

        public DataFrame(FrameType type, Command command, int parametersSize = 0)
        {
            if (parametersSize > 256)
            {
                throw new ArgumentOutOfRangeException("Parameters are too big");
            }

            var dataSize = parametersSize + 5;
            Data = new Span<byte>(new byte[dataSize]);
            Data[0] = (byte)FrameHeader.SOF;
            InfoSize = (byte) (parametersSize + 3);
            Type = type;
            Command = command;
        }

        public DataFrame(Span<byte> bytes)
        {
            Data = bytes;
            AssertFrame();
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
            var checksumable = Data.Slice(1, Data.Length - 2);
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

        public static bool IsFrameChecksumValid(Memory<byte> memory)
        {
            return new DataFrame(memory.Span).IsCheckSumValid;
        }
    }
}