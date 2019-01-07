using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZwaveExperiments.SerialProtocol
{
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    enum SerialMessageHeader : byte
    {
        /// <summary>
        /// Start of frame, signal a data frame.
        /// </summary>
        StartOfFrame = 0x01,
        
        /// <summary>
        /// Previous message acknowledgment
        /// </summary>
        Acknowledged = 0x06,
        
        /// <summary>
        /// Previous message was an error
        /// </summary>
        NotAcknowledged = 0x15,
        
        /// <summary>
        /// Retransmission request
        /// </summary>
        ResendRequest = 0x18
    }

    enum SerialMessageType
    {
        Request = 0x00,
        Response = 0x01
    }

    interface ISerialCommunication
    {
        IObservable<PooledSerialMessage> UnsolicitedMessages { get; }

        Task WriteAsync(PooledSerialMessage message, CancellationToken cancellationToken = default);
        Task<PooledSerialMessage> QueryAsync(PooledSerialMessage query, CancellationToken cancellationToken = default);
    }

    struct PooledSerialMessage : IDisposable
    {
        private readonly ArrayPool<byte> pool;
        public byte[] Data { get; }

        public PooledSerialMessage(ArrayPool<byte> pool, byte[] data)
        {
            this.pool = pool;
            Data = data;
        }

        public void Dispose()
        {
            pool.Return(Data);
        }
    }
}
