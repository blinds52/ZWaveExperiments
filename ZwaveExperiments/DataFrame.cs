using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace ZwaveExperiments
{
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    enum FrameHeader : byte
    {
        /// <summary>
        /// Start of frame, signal a data frame.
        /// </summary>
        SOF = 0x01,
        
        /// <summary>
        /// Previous message acknowledgment
        /// </summary>
        ACK = 0x06,
        
        /// <summary>
        /// Previous message was an error
        /// </summary>
        NAK = 0x15,
        
        /// <summary>
        /// Retransmission request
        /// </summary>
        CAN = 0x18
    }

    enum FrameType : byte
    {
        Request = 0x00,
        Response = 0x01
    }

    enum Command : byte
    {
        NoOperation = 0x00,
        GetHomeId = 0x20
    }

    ref struct DataFrame
    {
        const byte HEADER = (byte) FrameHeader.SOF;

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
            Debug.Assert(bytes.Length >= 5, $"Invalid frame, length = {bytes.Length}");
            Debug.Assert(bytes[0] == HEADER, $"Not a data frame, [0] = 0x{bytes[0]:X2}");
            Debug.Assert(bytes.Length > 256 + 2, $"Frame is too big");
            Debug.Assert(bytes[1] == bytes.Length - 2, $"Invalid length {bytes[1]} expected {bytes.Length - 2}");

            Data = bytes;
        }

        public byte ComputeCheckSum()
        {
            var checksumable = Data.Slice(1, Data.Length - 2);
            return ZWaveCheckSum.Compute(checksumable);
        }

        public void UpdateCheckSum()
        {
            CheckSum = ComputeCheckSum();
        }
    }
}