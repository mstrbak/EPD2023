using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ProvisioningDemo
{
    internal class Program
    {
        private static async Task Main(string[] args)
        {
            var servicesCollection = new ServiceCollection()
                .AddLogging(loggingBuilder => loggingBuilder
                    .SetMinimumLevel(LogLevel.Debug)
                    .AddConsole()
                    .AddDebug()
                );

            var serviceProvider = servicesCollection.BuildServiceProvider();

            var logger = serviceProvider.GetService<ILogger<Program>>()!;
            using var cts = new CancellationTokenSource();

            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cts.Cancel();
                Console.WriteLine("Cancellation requested; will exit.");
            };

            Console.WriteLine($"Press Control+C at any time to quit the sample.");

            try
            {
                var demo = new ProvisioningDemo(logger);
                await demo.RunDemo(cts.Token);
            }
            catch (OperationCanceledException) { }
        }
    }
}