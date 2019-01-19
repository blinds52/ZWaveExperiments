using Serilog;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;

namespace ZwaveExperiments.SerialProtocol.LowLevel
{
    interface ISerialPortWrapper
    {
        int BytesToRead { get; }
        Stream BaseStream { get; }
    }

    class SerialPortWrapper: ISerialPortWrapper
    {
        private readonly SerialPort serialPort;

        public int BytesToRead => serialPort.BytesToRead;

        public Stream BaseStream => serialPort.BaseStream;

        public SerialPortWrapper(SerialPort serialPort)
        {
            this.serialPort = serialPort;
        }
    }

    class SerialCommunication2 : IDisposable
    {
        readonly static ILogger log = Log.Logger.ForContext<SerialCommunication2>();

        readonly ISerialPortWrapper serialPort;
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

        public SerialCommunication2(ISerialPortWrapper serialPort)
        {
            this.serialPort = serialPort ?? throw new ArgumentNullException(nameof(serialPort));

            frameStream = new FrameStream(serialPort.BaseStream);
            readTask = RunReadTask();
        }

        void Ignore(Task task)
        {
            // TODO: Log result
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
                    case FrameHeader.SOF:
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

                    case FrameHeader.ACK:
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

        public async Task Write(SerialDataFrame frame, CancellationToken cancellationToken)
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

    class SerialCommunication : ISerialCommunication, IDisposable
    {
        readonly ISerialPortWrapper serialPort;
        readonly Subject<SerialFrame> unsolicitedMessages;
        readonly Task dataPipeTask;
        readonly CancellationTokenSource disposeTokenSource = new CancellationTokenSource();
        readonly FrameStream frameStream;
        readonly SemaphoreSlim operationSemaphore = new SemaphoreSlim(1, 1);
        readonly AutoResetEvent newOperationEvent = new AutoResetEvent(false);
        Operation? currentOperation;

        struct Operation
        {
            public CancellationToken CancellationToken { get; }
            public bool IsQuery { get; }
            public TaskCompletionSource<SerialFrame> CompletionSource { get; }
            public SerialFrame MessageToSend { get; }

            public Operation(bool isQuery, SerialFrame messageToSend, CancellationToken cancellationToken)
            {
                CompletionSource = new TaskCompletionSource<SerialFrame>();
                MessageToSend = messageToSend;
                CancellationToken = cancellationToken;
                IsQuery = isQuery;
            }
        }

        public SerialCommunication(ISerialPortWrapper serialPort)
        {
            this.serialPort = serialPort ?? throw new ArgumentNullException(nameof(serialPort));
            unsolicitedMessages = new Subject<SerialFrame>();

            frameStream = new FrameStream(serialPort.BaseStream);
            dataPipeTask = ProcessStream();
        }

        async Task ProcessStream()
        {
            while (!disposeTokenSource.IsCancellationRequested)
            {
                await newOperationEvent.AsTask();
                Debug.Assert(currentOperation != null, nameof(currentOperation) + " != null");
                var operation = currentOperation.Value;
                currentOperation = null;

                var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(disposeTokenSource.Token, operation.CancellationToken);
                try
                {
                    await frameStream.WriteAsync(operation.MessageToSend, linkedToken.Token);
                    var ackResponse = await frameStream.ReadAsync(linkedToken.Token);
                    if (!operation.IsQuery || ackResponse.Header != FrameHeader.ACK)
                    {
                        operation.CompletionSource.SetResult(ackResponse);
                    }

                    var queryResponse = await frameStream.ReadAsync(linkedToken.Token);
                    operation.CompletionSource.SetResult(queryResponse);
                    if (queryResponse.Header == FrameHeader.SOF)
                    {
                        await frameStream.WriteAsync(SerialFrame.Ack, disposeTokenSource.Token);
                    }
                }
                catch (OperationCanceledException cancel)
                {
                    operation.CompletionSource.SetCanceled();
                    if (cancel.CancellationToken == disposeTokenSource.Token)
                    {
                        throw;
                    }
                }
            }
        }

        public IObservable<SerialFrame> UnsolicitedMessages => unsolicitedMessages;

        public Task WriteAsync(SerialFrame message, CancellationToken cancellationToken = default)
        {
            return OperationAsync(false, message, cancellationToken);
        }

        public Task<SerialFrame> QueryAsync(SerialFrame query, CancellationToken cancellationToken = default)
        {
            return OperationAsync(true, query, cancellationToken);
        }

        async Task<SerialFrame> OperationAsync(bool isQuery, SerialFrame message, CancellationToken cancellationToken)
        {
            await operationSemaphore.WaitAsync(cancellationToken);
            try
            {
                var operation = new Operation(isQuery, message, cancellationToken);
                currentOperation = operation;
                newOperationEvent.Set();
                return await operation.CompletionSource.Task;
            }
            finally
            {
                operationSemaphore.Release();
            }
        }

        public void Dispose()
        {
            disposeTokenSource.Cancel();
            operationSemaphore.Wait();
            operationSemaphore.Dispose();
            dataPipeTask.Wait();
            disposeTokenSource.Dispose();
        }
    }
}