using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Pipelines;
using System.Reactive.Subjects;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZwaveExperiments.SerialProtocol.LowLevel
{
    enum SerialMessageHeader : byte
    {
        /// <summary>
        /// Start of frame (SOF), signal a data frame.
        /// </summary>
        StartOfFrame = 0x01,
        
        /// <summary>
        /// Previous message acknowledgment. (ACK)
        /// </summary>
        Acknowledged = 0x06,
        
        /// <summary>
        /// Previous message was an error. (NAK)
        /// </summary>
        NotAcknowledged = 0x15,
        
        /// <summary>
        /// Retransmission request (CAN)
        /// </summary>
        ResendRequest = 0x18
    }

    enum SerialMessageType : byte
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

    class MySegment: ReadOnlySequenceSegment<byte>
    {
        public MySegment()
        {
        }
    }

    static class SerialCommunicationParsing
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static SequencePosition? EndOfFrame(in ReadOnlySequence<byte> source)
        {
            if (source.Length == 0)
            {
                return null;
            }

            if (source.IsSingleSegment)
            {
                var span = source.First.Span;
                var header = (SerialMessageHeader) span[0];
                if (header != SerialMessageHeader.StartOfFrame)
                {
                    return source.GetPosition(1);
                }

                if (span.Length < 2)
                {
                    return null;
                }

                var length = span[1];
                var end = length + 2;
                if (end > span.Length)
                {
                    return null;
                }

                return source.GetPosition(end);
            }
            else
            {
                return EndOfFrameMultiSegment(source);
            }
        }

        private static SequencePosition? EndOfFrameMultiSegment(in ReadOnlySequence<byte> source)
        {
            SerialMessageHeader? header = null;
            SequencePosition position = source.Start;
            while (source.TryGet(ref position, out var memory))
            {
                var span = memory.Span;
                int spanPosition = 0;
                if (header == null)
                {
                    if (span.Length == 0)
                    {
                        continue;
                    }
                    header = (SerialMessageHeader) span[0];
                    if (header != SerialMessageHeader.StartOfFrame)
                    {
                        return source.GetPosition(1);
                    }

                    spanPosition = 1;
                }

                if (span.Length > spanPosition)
                {
                    var length = span[spanPosition];
                    var end = length + 2;
                    if (end > source.Length)
                    {
                        return null;
                    }

                    return source.GetPosition(end);
                }
            }

            return null;
        }
    }

    class SerialCommunication : ISerialCommunication, IDisposable
    {
        private readonly Stream stream;
        private readonly ArrayPool<byte> pool;
        private readonly Subject<PooledSerialMessage> unsolicitedMessages;
        private readonly Task dataPipeTask;
        private readonly CancellationTokenSource disposeTokenSource = new CancellationTokenSource();

        public SerialCommunication(Stream stream, ArrayPool<byte> pool = null)
        {
            this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
            this.pool = pool ?? ArrayPool<byte>.Create();
            unsolicitedMessages = new Subject<PooledSerialMessage>();

            dataPipeTask = ProcessStream(stream);
        }

        private Task ProcessStream(Stream socket)
        {
            var pipe = new Pipe();
            var writing = FillPipeAsync(socket, pipe.Writer, CancellationToken.None);
            var reading = ReadPipeAsync(pipe.Reader);

            return Task.WhenAll(reading, writing);
        }

        private async Task ReadPipeAsync(PipeReader reader)
        {
            while (true)
            {
                ReadResult result = await reader.ReadAsync();

                ReadOnlySequence<byte> buffer = result.Buffer;
                SequencePosition? position = null;

                do
                {
                    position = SerialCommunicationParsing.EndOfFrame(buffer);

                    if (position != null)
                    {
                        // Process the line
                        ProcessLine(buffer.Slice(0, position.Value));

                        // Skip the line + the \n character (basically position)
                        buffer = buffer.Slice(buffer.GetPosition(1, position.Value));
                    }
                }
                while (position != null);

                // Tell the PipeReader how much of the buffer we have consumed
                reader.AdvanceTo(buffer.Start, buffer.End);

                // Stop reading if there's no more data coming
                if (result.IsCompleted)
                {
                    break;
                }
            }

            // Mark the PipeReader as complete
            reader.Complete();
        }

        private void ProcessLine(ReadOnlySequence<byte> readOnlySequence)
        {
            throw new NotImplementedException();
        }

        private async Task FillPipeAsync(Stream stream, PipeWriter writer, CancellationToken ct)
        {
            const int minimumBufferSize = 256 + 2;

            try
            {
                while (true)
                {
                    if (ct.IsCancellationRequested)
                    {
                        break;
                    }

                    var memory = writer.GetMemory(minimumBufferSize);
                    try
                    {
                        int bytesRead = await stream.ReadAsync(memory, ct);
                        if (bytesRead == 0)
                        {
                            continue;
                        }

                        // Tell the PipeWriter how much was read from the Socket
                        writer.Advance(bytesRead);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine(ex.ToString());
                        break;
                    }

                    // Make the data available to the PipeReader
                    FlushResult result = await writer.FlushAsync(ct);

                    if (result.IsCompleted)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }

            // Tell the PipeReader that there's no more data coming
            writer.Complete();
        }

        public IObservable<PooledSerialMessage> UnsolicitedMessages => unsolicitedMessages;

        public Task WriteAsync(PooledSerialMessage message, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<PooledSerialMessage> QueryAsync(PooledSerialMessage query, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            disposeTokenSource.Cancel();
            dataPipeTask.Wait();
        }
    }

    struct PooledSerialMessage : IDisposable
    {
        public ArrayPool<byte> Pool { get; }
        public ArraySegment<byte> Data { get; }

        public PooledSerialMessage(ArrayPool<byte> pool, ArraySegment<byte> data)
        {
            Pool = pool;
            Data = data;
        }

        public void Dispose()
        {
            Pool.Return(Data.Array);
        }
    }
}
