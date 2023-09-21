using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ReconnectionDemo
{
    internal class Program
    {
        /// <summary>
        /// A sample for illustrating how a device should handle connection status updates.
        /// </summary>
        public static async Task<int> Main(string[] args)
        {
            var servicesCollection = new ServiceCollection()
                .AddLogging(loggingBuilder => loggingBuilder
                    .SetMinimumLevel(LogLevel.Debug)
                    .AddConsole()
                    .AddDebug()
                );

            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();


            servicesCollection.Configure<Settings>(configuration.GetSection("Settings"),
                options => options.ErrorOnUnknownConfiguration = true);

            var serviceProvider = servicesCollection.BuildServiceProvider();

            var settings = serviceProvider.GetService<IOptions<Settings>>()!.Value;
            var logger = serviceProvider.GetService<ILogger<Program>>()!;
            
            // Run the sample
            var runningTime = TimeSpan.FromSeconds(settings.ApplicationRunningTime);

            var sample = new ReconnectionDemo(settings, logger);
            await sample.RunSampleAsync(runningTime);

            logger.LogInformation("Done.");
            return 0;
        }
    }
}