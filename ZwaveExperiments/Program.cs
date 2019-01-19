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
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using ZwaveExperiments.SerialProtocol.Framing;

namespace ZwaveExperiments
{
    static class Program
    {
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

            var serialFrame = new SerialDataFrame(SerialMessageType.Request, SerialCommand.DiscoveryNodes);
            using(var comPort = new SerialPort("COM3"))
            {
                comPort.Open();

                using (var comm = new SerialFraming(comPort.BaseStream))
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
    }
}