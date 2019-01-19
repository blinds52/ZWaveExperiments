using System;
using System.Diagnostics;

namespace ZwaveExperiments.SerialProtocol.Framing
{
    struct SerialDataFrame
    {
        const byte HEADER = (byte)SerialFrameHeader.SOF;

        public SerialMessageType MessageType
        {
            get => (SerialMessageType) Data[2];
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

        public SerialDataFrame(SerialMessageType type, SerialCommand command, int parametersSize = 0)
        {
            if (parametersSize > 256)
            {
                throw new ArgumentOutOfRangeException("Parameters are too big");
            }

            var dataSize = parametersSize + 5;
            Data = new byte[dataSize];
            Data[0] = (byte)SerialFrameHeader.SOF;
            InfoSize = (byte) (parametersSize + 3);
            MessageType = type;
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