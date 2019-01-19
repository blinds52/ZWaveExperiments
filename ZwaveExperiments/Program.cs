using Serilog;
using System;
using System.Reactive;
using System.Buffers;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using ZwaveExperiments.SerialProtocol;
using ZwaveExperiments.SerialProtocol.LowLevel;
using System.Reactive.Concurrency;
using System.Reactive.Linq;

namespace ZwaveExperiments
{
    static class Program
    {
        static Task XWrite(ZWaveReader stream)
        {
            var test = new DataFrame(FrameType.Request, Command.GetHomeId);
            test.UpdateCheckSum();
            test.Data.BinaryDump("To write");
            return stream.WriteDataFrame(test);
        }

        static async Task Main()
        {
            var log = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {SourceContext}{NewLine}{Exception}")
                .CreateLogger();
            Log.Logger = log;
            
            while (Console.KeyAvailable)
            {
                Console.ReadKey();
            }

            var serialFrame = new SerialDataFrame(FrameType.Request, SerialCommand.DiscoveryNodes);
            using(var comPort = new SerialPort("COM3"))
            {
                comPort.Open();

                using (var comm = new SerialCommunication2(new SerialPortWrapper(comPort)))
                {
                    comm.ReceivedFrames.ObserveOn(Scheduler.Default).Subscribe(f =>
                    {
                        f.Data.BinaryDump();
                    });

                    Console.WriteLine("Sending...");
                    await comm.Write(serialFrame, CancellationToken.None);
                    Console.WriteLine("Sent !");

                    while (!Console.KeyAvailable)
                    {
                        await Task.Delay(250);
                    }
                }
            };

            Console.WriteLine("End.");
            Console.ReadLine();
        }

        async static Task Old()
        {
            /*
            var test = new DataFrame(FrameType.Request, Command.GetHomeId);
            test.UpdateCheckSum();
            test.Data.BinaryDump("To write");
        
            var writeBuffer = new byte[256 + 2];
            var readBuffer = new byte[256 + 2];
            var ackBuffer = new byte[1] { (byte)FrameHeader.Ack };
            */
            using (var p = new SerialPort("COM3"))
            {
                p.Open();
                p.DiscardInBuffer();

                var reader = new ZWaveReader(p.BaseStream);
                await XWrite(reader);
                var ackMsg = await reader.ReadNextAsync(CancellationToken.None);
                ackMsg.BinaryDump("Ack");
                var dataMsg = await reader.ReadNextAsync(CancellationToken.None);
                dataMsg.BinaryDump("Data");

                /*
                while (p.BytesToRead != 0)
                {
                    var rr = p.Read(readBuffer, 0, readBuffer.Length);
                    var s = readBuffer.AsSpan(0, rr);
                    s.BinaryDump("Before");
                }
                
                test.Data.CopyTo(writeBuffer);
                p.Write(writeBuffer, 0, test.Data.Length);
                Thread.Sleep(200);
                
                var bytesRead = p.Read(readBuffer, 0, readBuffer.Length);
                var spanRead = readBuffer.AsSpan(0, bytesRead);
                spanRead.BinaryDump("read");
                p.Write(ackBuffer, 0, ackBuffer.Length);
                
                while (p.BytesToRead != 0)
                {
                    var rr = p.Read(readBuffer, 0, readBuffer.Length);
                    var s = readBuffer.AsSpan(0, rr);
                    s.BinaryDump("After");
                }
                */
            }

            Console.WriteLine("End.");
            Console.ReadLine();
        }
    }



    class ZWaveReader
    {
        ArrayPool<byte> bytesPool = ArrayPool<byte>.Create();
        static byte[] ackBuffer = new byte[1] {(byte) FrameHeader.ACK};

        Stream stream;
        ArraySegment<byte>? remainingBytes;

        public ZWaveReader(Stream stream)
        {
            this.stream = stream;
        }

        private async Task<ArraySegment<byte>> Read(ArraySegment<byte> segment, CancellationToken ct)
        {
            var offset = segment.Offset + segment.Count;
            var count = segment.Array.Length - offset;
            var bytesRead = await stream.ReadAsync(segment.Array, offset, count, ct);
            return new ArraySegment<byte>(segment.Array, segment.Offset, segment.Count + bytesRead);
        }

        bool TryReadFrameArray(ReadOnlySpan<byte> span, out byte[] frame)
        {
            if (TryReadFrame(span, out var frameSpan))
            {
                frame = frameSpan.ToArray();
                return true;
            }
            else
            {
                frame = null;
                return false;
            }
        }

        bool TryReadFrame(ReadOnlySpan<byte> span, out ReadOnlySpan<byte> frame)
        {
            if (span.Length == 0)
            {
                frame = Span<byte>.Empty;
                return false;
            }

            var frameHeader = (FrameHeader) span[0];
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

        public Task WriteDataFrame(DataFrame frame)
        {
            frame.UpdateCheckSum();
            var bytesToWrite = frame.Data.Length;
            var buffer = bytesPool.Rent(bytesToWrite);
            frame.Data.CopyTo(buffer);
            return Inner();

            async Task Inner()
            {
                try
                {
                    await stream.WriteAsync(buffer, 0, bytesToWrite);
                }
                finally
                {
                    bytesPool.Return(buffer);
                }
            }
        }

        public async Task<byte[]> ReadNextAsync(CancellationToken ct)
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

            while (!ct.IsCancellationRequested)
            {
                buffer = await Read(buffer, ct);
                if (TryReadFrameArray(buffer, out var frame))
                {
                    if (frame.Length == buffer.Count)
                    {
                        remainingBytes = null;
                        bytesPool.Return(buffer.Array);
                    }
                    else
                    {
                        remainingBytes = new ArraySegment<byte>(buffer.Array, buffer.Offset + frame.Length,
                            buffer.Count - frame.Length);
                    }

                    var frameHeader = (FrameHeader) frame[0];
                    if (frameHeader == FrameHeader.SOF)
                    {
                        await stream.WriteAsync(ackBuffer, 0, ackBuffer.Length);
                    }

                    return frame;
                }
            }

            return null;
        }
    }
    /*
    async Task ProcessLinesAsync(Stream socket)
    {
        var pipe = new Pipe();
        Task writing = FillPipeAsync(socket, pipe.Writer, CancellationToken.None);
        Task reading = ReadPipeAsync(pipe.Reader);

        return Task.WhenAll(reading, writing);
    }

    async Task ReadPipeAsync(PipeReader reader)
    {
        while (true)
        {
            ReadResult result = await reader.ReadAsync();

            ReadOnlySequence<byte> buffer = result.Buffer;
            SequencePosition? position = null;

            do
            {

                position = buffer.PositionOf((byte)'\n');

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

    async Task FillPipeAsync(Stream stream, PipeWriter writer, CancellationToken ct)
    {
        const int minimumBufferSize = 256 + 2;

        while (true)
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            var memory = writer.GetMemory(minimumBufferSize);
            if (!MemoryMarshal.TryGetArray(memory, out ArraySegment<byte> segment))
            {
                throw new Exception("???");
            }

            try
            {
                int bytesRead = await stream.ReadAsync(segment.Array, segment.Offset, segment.Count);
                if (bytesRead == 0)
                {
                    continue;
                }
                // Tell the PipeWriter how much was read from the Socket
                writer.Advance(bytesRead);
            }
            catch (Exception ex)
            {
                ex.Dump();
                //LogError(ex);
                break;
            }

            // Make the data available to the PipeReader
            FlushResult result = await writer.FlushAsync();

            if (result.IsCompleted)
            {
                break;
            }
        }

        // Tell the PipeReader that there's no more data coming
        writer.Complete();
    }
    */
}