using System.Security.Cryptography;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Provisioning.Client;
using Microsoft.Azure.Devices.Shared;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;
using Message = Microsoft.Azure.Devices.Client.Message;
namespace ProvisioningDemo
{
    internal class ProvisioningDemo
    {
        private readonly ILogger<Program> _logger;

        public ProvisioningDemo(ILogger<Program> logger)
        {
            _logger = logger;
        }

        public async Task RunDemo(CancellationToken cancellationToken)
        {
            var rand = new Random();
            var deviceId = $"device-{rand.Next(1, 10)}";

            string derivedPrimaryKey = GenerateDerivedKeyFromSymmetric(deviceId, Settings.SymmetricKeyPrimary);

            using var security = new SecurityProviderSymmetricKey(deviceId, derivedPrimaryKey, null);

            var registrationResult = await RegisterDevice(deviceId, security, cancellationToken);

            if (registrationResult.Status != ProvisioningRegistrationStatusType.Assigned)
            {
                _logger.LogError($"Registration status did not assign a hub, so exiting this sample.");
                return;
            }
            _logger.LogInformation($"Device {registrationResult.DeviceId} registered to {registrationResult.AssignedHub}.");

            _logger.LogInformation("Creating symmetric key authentication for IoT Hub...");
            var auth = new DeviceAuthenticationWithRegistrySymmetricKey(registrationResult.DeviceId, security.GetPrimaryKey());

            _logger.LogInformation("Connecting to the Iot Hub...");
            await using var deviceClient = DeviceClient.Create(registrationResult.AssignedHub, auth, Settings.TransportType);

            await deviceClient!.SetMethodHandlerAsync("IsAlive", IsAlive, deviceClient, cancellationToken);
            await SendDeviceToCloudMessagesAsync(deviceClient, cancellationToken);
            await deviceClient.CloseAsync(cancellationToken);
        }

        private string GenerateDerivedKeyFromSymmetric(string deviceId, string symmetricKey)
        {
            var hmac = new HMACSHA256(Convert.FromBase64String(symmetricKey));
            var sig = hmac.ComputeHash(Encoding.ASCII.GetBytes(deviceId));
            var derivedKey = Convert.ToBase64String(sig);
            return derivedKey;
        }

        private async Task<DeviceRegistrationResult> RegisterDevice(string deviceId,
            SecurityProviderSymmetricKey security, CancellationToken cancellationToken)
        {
            using var transportHandler = Utils.CreateTransportHandler(Settings.TransportType);

            _logger.LogInformation($"Initializing the device provisioning client...");

            ProvisioningDeviceClient provClient = ProvisioningDeviceClient.Create(
                Settings.GlobalDeviceEndpoint,
                Settings.IdScope,
                security,
                transportHandler);

            _logger.LogInformation($"Initialized for registration Id {security.GetRegistrationID()}.");
            _logger.LogInformation("Registering with the device provisioning service... ");

            var registrationResult = await provClient.RegisterAsync(cancellationToken);
            _logger.LogDebug(Utils.DumpRegistration(registrationResult));

            return registrationResult;
        }

        /// <summary>
        /// Callback method for the 'IsAlive' command
        /// </summary>
        /// <returns></returns>
        /// <remarks>
        /// </remarks>
        private Task<MethodResponse> IsAlive(MethodRequest methodRequest, object userContext)
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
