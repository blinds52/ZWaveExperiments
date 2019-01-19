using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ZwaveExperiments.SerialProtocol.LowLevel
{
    class FrameStream
    {
        readonly ArrayPool<byte> bytesPool = ArrayPool<byte>.Create();
        static readonly byte[] AckBuffer = { (byte)FrameHeader.ACK };

        readonly Stream stream;

        ArraySegment<byte>? remainingBytes;
    
        public FrameStream(Stream stream)
        {
            this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
        }

        async Task<ArraySegment<byte>> Read(ArraySegment<byte> segment, CancellationToken ct)
        {
            var offset = segment.Offset + segment.Count;
            var count = segment.Array.Length - offset;
            var bytesRead = await stream.ReadAsync(segment.Array, offset, count, ct);
            return new ArraySegment<byte>(segment.Array, segment.Offset, segment.Count + bytesRead);
        }

        PooledSerialFrame? TryReadFrame(ArraySegment<byte> buffer, out bool returnMemory)
        {
            if (!TryReadFrameSpan(buffer, out var frameSpan))
            {
                returnMemory = true;
                return null;
            }

            // No data, not allocation is involved
            if (frameSpan.Length == 1)
            {
                Debug.Assert((FrameHeader) frameSpan[0] != FrameHeader.SOF);
                returnMemory = true;
                return new PooledSerialFrame((FrameHeader) frameSpan[0]);
            }

            // The full buffer is a data frame, we can reuse it
            if (buffer.Offset == 0 && frameSpan.Length == buffer.Count)
            {
                returnMemory = false;
                return new PooledSerialFrame(bytesPool, buffer);
            }

            // Only part of the buffer is a data frame, we need to allocate from the pool to transmit it
            Debug.Assert((FrameHeader) frameSpan[0] == FrameHeader.SOF);
            var array = bytesPool.Rent(frameSpan.Length);
            frameSpan.CopyTo(array);
            returnMemory = true;
            return new PooledSerialFrame(bytesPool, new ArraySegment<byte>(array, 0, frameSpan.Length));
        }

        bool TryReadFrameSpan(ReadOnlySpan<byte> span, out ReadOnlySpan<byte> frame)
        {
            if (span.Length == 0)
            {
                frame = Span<byte>.Empty;
                return false;
            }

            var frameHeader = (FrameHeader)span[0];
            if (frameHeader != FrameHeader.SOF)
            {
                frame = span.Slice(0, 1);
                return true;
            }

            if (span.Length < 5)
            {
                frame = Span<byte>.Empty;
                return false;
            }

            var length = span[1];
            if (span.Length < length + 2)
            {
                frame = Span<byte>.Empty;
                return false;
            }
        
            frame = span.Slice(0, length + 2);
            return true;
        }

        public async Task WriteAsync(PooledSerialFrame frame, CancellationToken ct = default)
        {
            ArraySegment<byte> buffer;
            var rented = false;
            if (frame.Data.Count == 0)
            {
                var bufferArray = bytesPool.Rent(1);
                rented = true;
                bufferArray[0] = (byte)frame.Header;
                buffer = new ArraySegment<byte>(bufferArray, 0, 1);
            }
            else
            {
                buffer = frame.Data;
            }

            try
            {
                await stream.WriteAsync(buffer.Array, buffer.Offset, buffer.Count, ct);
            }
            finally
            {
                if (rented)
                {
                    bytesPool.Return(buffer.Array);
                }
            }
        }

        public async Task<PooledSerialFrame> ReadAsync(CancellationToken ct = default)
        {
            ArraySegment<byte> buffer;
            if (remainingBytes != null)
            {
                buffer = remainingBytes.Value;
            }
            else
            {
                var bufferArray = bytesPool.Rent(256 + 2);
                buffer = new ArraySegment<byte>(bufferArray, 0, 0);
            }
        
            while (true)
            {
                buffer = await Read(buffer, ct);
                var maybeFrame = TryReadFrame(buffer, out var returnMemory);

                if (maybeFrame != null)
                {
                    var frame = maybeFrame.Value;
                    if (returnMemory && frame.Length == buffer.Count)
                    {
                        remainingBytes = null;
                        bytesPool.Return(buffer.Array);
                    }
                    else
                    {
                        remainingBytes = new ArraySegment<byte>(buffer.Array, buffer.Offset + frame.Length, buffer.Count - frame.Length);
                    }

                    if (frame.Header == FrameHeader.SOF)
                    {
                        await stream.WriteAsync(AckBuffer, 0, AckBuffer.Length, ct);
                    }
                                
                    return frame;
                }

                if (buffer.Count == 0)
                {
                    Debug.Fail("Shouldn't happen");
                }

                ct.ThrowIfCancellationRequested();
            }
        }
    }
}