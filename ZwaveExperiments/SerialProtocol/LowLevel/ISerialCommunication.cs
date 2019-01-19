using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZwaveExperiments.SerialProtocol.LowLevel
{
    interface ISerialCommunication
    {
        IObservable<SerialFrame> UnsolicitedMessages { get; }

        Task WriteAsync(SerialFrame message, CancellationToken cancellationToken = default);
        Task<SerialFrame> QueryAsync(SerialFrame query, CancellationToken cancellationToken = default);
    }
}
