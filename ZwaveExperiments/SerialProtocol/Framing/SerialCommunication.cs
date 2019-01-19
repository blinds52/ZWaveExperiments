using Serilog;
using System;
using System.IO;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;

namespace ZwaveExperiments.SerialProtocol.Framing
{

    class SerialFraming : ISerialFraming, IDisposable
    {
        readonly static ILogger log = Log.Logger.ForContext<SerialFraming>();

        readonly Subject<SerialDataFrame> messagesReceived = new Subject<SerialDataFrame>();
        readonly CancellationTokenSource disposeTokenSource = new CancellationTokenSource();
        readonly FrameStream frameStream;
        readonly Task readTask;
        readonly SemaphoreSlim writeSemaphore = new SemaphoreSlim(1, 1);
        TransmitedFrame? frameBeingSent = null;

        struct TransmitedFrame
        {
            public SerialDataFrame Data { get; }
            public TaskCompletionSource<bool> CompletionSource { get; }

            public TransmitedFrame(SerialDataFrame data)
            {
                Data = data;
                CompletionSource = new TaskCompletionSource<bool>();
            }
        }

        public SerialFraming(Stream stream)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            frameStream = new FrameStream(stream);
            readTask = RunReadTask();
        }

        void Ignore(Task task)
        {
            task.ContinueWith(OnFailed, TaskContinuationOptions.OnlyOnFaulted);

            void OnFailed(Task t)
            {
                log.Error(t.Exception, "Ignored answer task failed");
            }
        }

        async Task RunReadTask()
        {
            log.Information("Starting");
            while (!disposeTokenSource.IsCancellationRequested)
            {
                var newFrame = await frameStream.ReadAsync(disposeTokenSource.Token);
                log.Verbose("Received frame with header {header}", newFrame.Header);
                switch (newFrame.Header)
                {
                    case SerialFrameHeader.SOF:
                        var dataFrame = newFrame.AsSerialDataFrame();
                        if (!dataFrame.IsValid)
                        {
                            Ignore(frameStream.WriteAsync(SerialFrame.Nak, CancellationToken.None));
                        }
                        else
                        {
                            Ignore(frameStream.WriteAsync(SerialFrame.Ack, CancellationToken.None));
                            messagesReceived.OnNext(dataFrame);
                        }

                        break;

                    case SerialFrameHeader.ACK:
                        if (frameBeingSent == null)
                        {
                            log.Warning("Received unexpected ack");
                        }
                        else
                        {
                            log.Verbose("Received valid ACK for current data frame");
                            frameBeingSent.Value.CompletionSource.TrySetResult(true);
                        }
                        
                        break;

                    default:
                        log.Warning("Received frame with unknown header {header}", newFrame.Header);
                        break;
                }
            }
            log.Information("Ended");
        }

        public async Task Write(SerialDataFrame frame, CancellationToken cancellationToken = default)
        {
            log.Information("Sending DataFrame for command {command}", frame.Command);

            await writeSemaphore.WaitAsync(cancellationToken);
            try
            {
                frame.UpdateCheckSum();
                frameBeingSent = new TransmitedFrame(frame);
                log.Verbose("Sending bytes for command {command}", frame.Command);
                await frameStream.WriteAsync(frame.AsSerialFrame(), cancellationToken);
                log.Verbose("Awaiting ACK for command {command}", frame.Command);
                await frameBeingSent.Value.CompletionSource.Task;
                frameBeingSent = null;
            }
            finally
            {
                writeSemaphore.Release();
            }
            log.Information("Sent with success");
        }

        public IObservable<SerialDataFrame> ReceivedFrames => messagesReceived;

        public void Dispose()
        {
            disposeTokenSource.Cancel();
            readTask.Wait();
            messagesReceived.Dispose();
        }
    }
}