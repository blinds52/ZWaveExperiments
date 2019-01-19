using System;
using System.Threading;
using System.Threading.Tasks;

namespace ZwaveExperiments.SerialProtocol.Framing
{
    interface ISerialFraming
    {
        IObservable<SerialDataFrame> ReceivedFrames { get; }

        Task Write(SerialDataFrame frame, CancellationToken cancellationToken = default);
    }
}
