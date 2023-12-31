﻿using Microsoft.Azure.Devices.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;
using System.Threading;
using Message = Microsoft.Azure.Devices.Client.Message;

namespace RoutingDemo
{
    internal class Program
    {
        private static ILogger<Program> _logger;

        private static async Task Main(string[] args)
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

            // Send messages to the simulated device. Each message will contain a randomly generated
            //   Temperature and Humidity.
            // The "level" of each message is set randomly to "storage", "critical", or "normal".
            // The messages are routed to different endpoints depending on the level, temperature, and humidity.

            var settings = serviceProvider.GetService<IOptions<Settings>>()!.Value;
            _logger = serviceProvider.GetService<ILogger<Program>>()!;

            await using var deviceClient = DeviceClient.CreateFromConnectionString(settings.DeviceConnectionString);

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (sender, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cts.Cancel();
                Console.WriteLine("Cancellation requested; will exit.");
            };

            Console.WriteLine($"Press Control+C at any time to quit the sample.");

            try
            {
                await deviceClient!.SetMethodHandlerAsync("IsAlive", IsAlive, deviceClient, cts.Token);
                await SendDeviceToCloudMessagesAsync(deviceClient, cts.Token);
                await deviceClient.CloseAsync(cts.Token);
            }
            catch (OperationCanceledException) { }
        }

        /// <summary>
        /// Callback method for the 'IsAlive' command
        /// </summary>
        /// <returns></returns>
        /// <remarks>
        /// </remarks>
        private static Task<MethodResponse> IsAlive(MethodRequest methodRequest, object userContext)
        {
            string data = Encoding.UTF8.GetString(methodRequest.Data);

            _logger.LogWarning(">>>>>>>>>> Executing IsAlive method. <<<<<<<<<<<<");
            _logger.LogInformation($"{DateTimeOffset.UtcNow.UtcDateTime}: {methodRequest.Name} '{data}'");

            string result = $"{{\"result\":\"Executed direct method: {methodRequest.Name}\"}}";
            return Task.FromResult(new MethodResponse(Encoding.UTF8.GetBytes(result), 200));
        }

        /// <summary>
        /// Send message to the Iot hub. This generates the object to be sent to the hub in the message.
        /// </summary>
        private static async Task SendDeviceToCloudMessagesAsync(DeviceClient deviceClient, CancellationToken token)
        {
            double minTemperature = 20;
            double minHumidity = 60;
            var rand = new Random();

            while (!token.IsCancellationRequested)
            {
                var currentTemperature = minTemperature + rand.NextDouble() * 15;
                var currentHumidity = minHumidity + rand.NextDouble() * 20;

                var (levelValue, infoString) = GetLevel(rand);

                var telemetryDataPoint = new
                {
                    temperature = currentTemperature,
                    humidity = currentHumidity,
                    pointInfo = infoString
                };

                // serialize the telemetry data and convert it to JSON.
                string telemetryDataString = JsonSerializer.Serialize(telemetryDataPoint);

                // Encode the serialized object using UTF-8 so it can be parsed by IoT Hub when
                // processing messaging rules.
                using var message = new Message(Encoding.UTF8.GetBytes(telemetryDataString))
                {
                    ContentEncoding = "utf-8",
                    ContentType = "application/json",
                };

                // This property will be used for routing query
                message.Properties.Add("level", levelValue);

                try
                {
                    // Submit the message to the hub.
                    await deviceClient.SendEventAsync(message, token);

                    Console.WriteLine("{0} > Sent message: {1}", DateTime.UtcNow, telemetryDataString);
                }
                catch (TaskCanceledException) { }
                catch (OperationCanceledException) { }

                try
                {
                    await Task.Delay(4000, token);
                }
                catch (TaskCanceledException) { }
                catch (OperationCanceledException) { }
            }
        }

        private static (string lv, string info) GetLevel(Random random)
        {
            var randomNumber = random.NextDouble();
            return randomNumber switch
            {
                >= 0.8 => ("critical", "This is a critical message."),
                (< 0.8) and (>= 0.7) => ("warning", "This is a warning message."),
                (< 0.7) and (>= 0.3) => ("storage", "This is a storage message."),
                _ => ("normal", "This is a normal message.")
            };
        }
    }
}