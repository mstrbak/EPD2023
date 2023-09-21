using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Client.Exceptions;
using Microsoft.Azure.Devices.Shared;
using Microsoft.Extensions.Logging;
using System.Text;

namespace ReconnectionDemo
{
    public class ReconnectionDemo
    {
        private static readonly Random RandomGenerator = new();
        private static readonly TimeSpan SleepDuration = TimeSpan.FromSeconds(5);

        private static readonly SemaphoreSlim InitSemaphore = new(1, 1);
        private static readonly ClientOptions ClientOptions = new() { SdkAssignsMessageId = SdkAssignsMessageId.WhenUnset };

        private readonly List<string> _deviceConnectionStrings;
        private readonly TransportType _transportType;

        private readonly ILogger _logger;

        // An UnauthorizedException is handled in the connection status change handler through its corresponding status change event.
        // We will ignore this exception when thrown by client API operations.
        private static readonly HashSet<Type> ExceptionsToBeRetried = new()
        {
            // Unauthorized exception conditions are handled by the ConnectionStatusChangeHandler in this sample and the sample will try
            // to reconnect indefinitely, so don't give up on an operation that sees this exception.
            typeof(UnauthorizedException),
        };
        private readonly IRetryPolicy _customRetryPolicy;


        // Mark these fields as volatile so that their latest values are referenced.
        private static volatile DeviceClient? _deviceClient;
        private static volatile ConnectionStatus _connectionStatus = ConnectionStatus.Disconnected;

        private static CancellationTokenSource? _appCancellation;

        private static long _localDesiredPropertyVersion = 1;

        public ReconnectionDemo(Settings settings, ILogger logger)
        {
            _logger = logger;
            _customRetryPolicy = new CustomRetryPolicy(ExceptionsToBeRetried, _logger);

            if (string.IsNullOrEmpty(settings.DeviceConnectionStringPrimary) || string.IsNullOrEmpty(settings.DeviceConnectionStringSecondary))
            {
                throw new ArgumentException("At least one connection string must be provided.", nameof(settings.DeviceConnectionStringPrimary));
            }
            _deviceConnectionStrings = new List<string> { settings.DeviceConnectionStringPrimary, settings.DeviceConnectionStringSecondary };
            _logger.LogInformation($"Supplied with {_deviceConnectionStrings.Count} connection string(s).");

            _transportType = settings.TransportType;
            _logger.LogInformation($"Using {_transportType} transport.");
        }

        private static bool IsDeviceConnected => _connectionStatus == ConnectionStatus.Connected;

        public async Task RunSampleAsync(TimeSpan sampleRunningTime)
        {
            _appCancellation = new CancellationTokenSource(sampleRunningTime);
            Console.CancelKeyPress += (sender, eventArgs) =>
            {
                eventArgs.Cancel = true;
                _appCancellation.Cancel();
                _logger.LogWarning("Sample execution cancellation requested; will exit.");
            };

            _logger.LogInformation($"Sample execution started, press Control+C to quit the sample.");

            try
            {
                await InitializeAndSetupClientAsync(_appCancellation.Token);
                await Task.WhenAll(SendMessagesAsync(_appCancellation.Token), ReceiveMessagesAsync(_appCancellation.Token));
            }
            catch (OperationCanceledException) { } // User canceled the operation
            catch (Exception ex)
            {
                _logger.LogError($"Unrecoverable exception caught, user action is required, so exiting: \n{ex}");
                _appCancellation.Cancel();
            }

            // Finally, close the client, but we can't use _appCancellation because it has been signaled to quit the app.
            if (_deviceClient != null)
            {
                await _deviceClient.CloseAsync(CancellationToken.None);
            }
            InitSemaphore.Dispose();
            _appCancellation.Dispose();
        }

        private async Task InitializeAndSetupClientAsync(CancellationToken cancellationToken)
        {
            if (ShouldClientBeInitialized(_connectionStatus))
            {
                // Allow a single thread to dispose and initialize the client instance.
                await InitSemaphore.WaitAsync(cancellationToken);
                try
                {
                    if (ShouldClientBeInitialized(_connectionStatus))
                    {
                        _logger.LogDebug($"Attempting to initialize the client instance, current status={_connectionStatus}");

                        // If the device client instance has been previously initialized, close and dispose it.
                        if (_deviceClient != null)
                        {
                            try
                            {
                                await _deviceClient.CloseAsync(cancellationToken);
                            }
                            catch (UnauthorizedException) { } // If the previous token is now invalid, this call may fail
                            _deviceClient.Dispose();
                        }

                        _deviceClient = DeviceClient.CreateFromConnectionString(_deviceConnectionStrings.First(), _transportType, ClientOptions);
                        _deviceClient.SetConnectionStatusChangesHandler(ConnectionStatusChangeHandlerAsync);
                        _deviceClient.SetRetryPolicy(_customRetryPolicy);
                        _logger.LogDebug("Initialized the client instance.");

                        // Force connection now.
                        // OpenAsync() is an idempotent call, it has the same effect if called once or multiple times on the same client.
                        //await _deviceClient.OpenAsync(cancellationToken);
                        //_logger.LogDebug($"The client instance has been opened.");

                        _logger.LogDebug($"Azure device client initialized. Trying to open a connection to the hub.");
                        try
                        {
                            await _deviceClient.OpenAsync(cancellationToken);
                            _logger.LogDebug("Azure device client instance has been opened");

                            // You will need to subscribe to the client callbacks any time the client is initialized.
                            await _deviceClient.SetDesiredPropertyUpdateCallbackAsync(HandleTwinUpdateNotificationsAsync, null, cancellationToken);
                            await _deviceClient!.SetMethodHandlerAsync("IsAlive", IsAlive, this, cancellationToken);
                            await _deviceClient!.SetMethodHandlerAsync("StartLongRunning", StartLongRunning, this, cancellationToken);

                            _logger.LogDebug("The client has subscribed to desired property update notifications.");
                        }
                        catch (IotHubException exception) when (exception is UnauthorizedException)
                        {
                            if (!_deviceConnectionStrings.Any())
                            {
                                _logger.LogError("Unable to connect to the IoT Hub.");
                                throw;
                            }
                        }
                        catch (IotHubException exception)
                        {
                            _logger.LogError($"Error [{exception.GetType()}] occurred, retrying.");
                            // currently we try to retry all IoT exceptions,
                            // except Unauthorized, because for that we need to re-register
                            // here we can limit the set of exceptions handled and rethrow the rest
                        }
                    }
                }
                finally
                {
                    InitSemaphore.Release();
                }
            }
        }

        /// <summary>
        /// Callback method for the 'IsAlive' command
        /// </summary>
        /// <returns></returns>
        /// <remarks>
        /// Method needs to be registered with the _iotClient.
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
        /// Callback method for the 'StartLongRunning' command
        /// </summary>
        /// <returns></returns>
        /// <remarks>
        /// Method needs to be registered with the _iotClient.
        /// </remarks>
        private async Task<MethodResponse> StartLongRunning(MethodRequest methodRequest, object userContext)
        {
            _logger.LogInformation(">>>>>>>>>> Executing StartLongRunning method. <<<<<<<<<<<<");
            await Task.Delay(20000);
            string data = Encoding.UTF8.GetString(methodRequest.Data);

            _logger.LogInformation($"{DateTimeOffset.UtcNow.UtcDateTime}: {methodRequest.Name} '{data}'");

            string result = $"{{\"result\":\"Executed direct method: {methodRequest.Name}\"}}";
            return await Task.FromResult(new MethodResponse(Encoding.UTF8.GetBytes(result), 200));
        }

        // It is not generally a good practice to have async void methods, however, DeviceClient.ConnectionStatusChangeHandlerAsync() event handler signature
        // has a void return type. As a result, any operation within this block will be executed unmonitored on another thread.
        // To prevent multi-threaded synchronization issues, the async method InitializeClientAsync being called in here first grabs a lock before attempting to
        // initialize or dispose the device client instance; the async method GetTwinAndDetectChangesAsync is implemented similarly for the same purpose.
        private async void ConnectionStatusChangeHandlerAsync(ConnectionStatus status, ConnectionStatusChangeReason reason)
        {
            _logger.LogDebug($"Connection status changed: status={status}, reason={reason}");
            _connectionStatus = status;

            switch (status)
            {
                case ConnectionStatus.Connected:
                    _logger.LogDebug("### The DeviceClient is CONNECTED; all operations will be carried out as normal.");

                    // Call GetTwinAndDetectChangesAsync() to retrieve twin values from the server once the connection status changes into Connected.
                    // This can get back "lost" twin updates in a device reconnection from status like Disconnected_Retrying or Disconnected.
                    //
                    // Howevever, considering how a fleet of devices connected to a hub may behave together, one must consider the implication of performing
                    // work on a device (e.g., get twin) when it comes online. If all the devices go offline and then come online at the same time (for example,
                    // during a servicing event) it could introduce increased latency or even throttling responses.
                    // For more information, see https://docs.microsoft.com/azure/iot-hub/iot-hub-devguide-quotas-throttling#traffic-shaping.
                    await GetTwinAndDetectChangesAsync(_appCancellation.Token);
                    _logger.LogDebug("The client has retrieved twin values after the connection status changes into CONNECTED.");
                    break;

                case ConnectionStatus.Disconnected_Retrying:
                    _logger.LogDebug("### The DeviceClient is retrying based on the retry policy. Do NOT close or open the DeviceClient instance.");
                    break;

                case ConnectionStatus.Disabled:
                    _logger.LogDebug("### The DeviceClient has been closed gracefully." +
                        "\nIf you want to perform more operations on the device client, you should dispose (DisposeAsync()) and then open (OpenAsync()) the client.");
                    break;

                case ConnectionStatus.Disconnected:
                    var waitingTime = 5000;
                    _logger.LogDebug($"Waiting {waitingTime}ms before trying to reconnect.");
                    await Task.Delay(waitingTime);

                    switch (reason)
                    {
                        case ConnectionStatusChangeReason.Bad_Credential:
                            // When getting this reason, the current connection string being used is not valid.
                            // If we had a backup, we can try using that.
                            _deviceConnectionStrings.RemoveAt(0);
                            if (_deviceConnectionStrings.Any())
                            {
                                _logger.LogWarning($"The current connection string is invalid. Trying another.");

                                try
                                {
                                    await InitializeAndSetupClientAsync(_appCancellation.Token);
                                }
                                catch (OperationCanceledException) { } // User canceled

                                break;
                            }

                            _logger.LogWarning("### The supplied credentials are invalid. Update the parameters and run again.");
                            _appCancellation.Cancel();
                            break;

                        case ConnectionStatusChangeReason.Device_Disabled:
                            _logger.LogWarning("### The device has been deleted or marked as disabled (on your hub instance)." +
                                "\nFix the device status in Azure and then create a new device client instance.");
                            //_appCancellation.Cancel();

                            try
                            {
                                await InitializeAndSetupClientAsync(_appCancellation.Token);
                            }
                            catch (OperationCanceledException) { } // User canceled

                            break;

                        case ConnectionStatusChangeReason.Retry_Expired:
                            _logger.LogWarning("### The DeviceClient has been disconnected because the retry policy expired." +
                                "\nIf you want to perform more operations on the device client, you should dispose (DisposeAsync()) and then open (OpenAsync()) the client.");

                            try
                            {
                                await InitializeAndSetupClientAsync(_appCancellation.Token);
                            }
                            catch (OperationCanceledException) { } // User canceled

                            break;

                        case ConnectionStatusChangeReason.Communication_Error:
                            _logger.LogWarning("### The DeviceClient has been disconnected due to a non-retry-able exception. Inspect the exception for details." +
                                "\nIf you want to perform more operations on the device client, you should dispose (DisposeAsync()) and then open (OpenAsync()) the client.");

                            try
                            {
                                await InitializeAndSetupClientAsync(_appCancellation.Token);
                            }
                            catch (OperationCanceledException) { } // User canceled

                            break;

                        default:
                            _logger.LogError("### This combination of ConnectionStatus and ConnectionStatusChangeReason is not expected, contact the client library team with logs.");
                            break;
                    }

                    break;

                default:
                    _logger.LogError("### This combination of ConnectionStatus and ConnectionStatusChangeReason is not expected, contact the client library team with logs.");
                    break;
            }
        }

        private async Task GetTwinAndDetectChangesAsync(CancellationToken cancellationToken)
        {
            // For the following call, we execute with a retry strategy with incrementally increasing delays between retry.
            Twin twin = await _deviceClient.GetTwinAsync(cancellationToken);
            _logger.LogInformation($"Device retrieving twin values: {twin.ToJson()}");

            TwinCollection twinCollection = twin.Properties.Desired;
            long serverDesiredPropertyVersion = twinCollection.Version;

            // Check if the desired property version is outdated on the local side.
            if (serverDesiredPropertyVersion > _localDesiredPropertyVersion)
            {
                _logger.LogDebug($"The desired property version cached on local is changing from {_localDesiredPropertyVersion} to {serverDesiredPropertyVersion}.");
                await HandleTwinUpdateNotificationsAsync(twinCollection, cancellationToken);
            }
        }

        private async Task HandleTwinUpdateNotificationsAsync(TwinCollection twinUpdateRequest, object userContext)
        {
            var reportedProperties = new TwinCollection();

            _logger.LogInformation($"Twin property update requested: \n{twinUpdateRequest.ToJson()}");

            // For the purpose of this sample, we'll blindly accept all twin property write requests.
            foreach (KeyValuePair<string, object> desiredProperty in twinUpdateRequest)
            {
                _logger.LogInformation($"Setting property {desiredProperty.Key} to {desiredProperty.Value}.");
                reportedProperties[desiredProperty.Key] = desiredProperty.Value;
            }

            _localDesiredPropertyVersion = twinUpdateRequest.Version;
            _logger.LogDebug($"The desired property version on local is currently {_localDesiredPropertyVersion}.");

            try
            {
                // For the purpose of this sample, we'll blindly accept all twin property write requests.
                await _deviceClient.UpdateReportedPropertiesAsync(reportedProperties, _appCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                // Fail gracefully on sample exit.
            }
        }

        private async Task SendMessagesAsync(CancellationToken cancellationToken)
        {
            int messageCount = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                if (IsDeviceConnected)
                {
                    _logger.LogInformation($"Device sending message {++messageCount} to IoT hub.");
                    using Message message = PrepareMessage(messageCount);
                    await _deviceClient.SendEventAsync(message, cancellationToken);
                    _logger.LogInformation($"Device sent message {messageCount} to IoT hub.");
                }

                await Task.Delay(SleepDuration, cancellationToken);
            }
        }

        private async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!IsDeviceConnected)
                {
                    await Task.Delay(SleepDuration, cancellationToken);
                    continue;
                }
                else if (_transportType == TransportType.Http1)
                {
                    // The call to ReceiveAsync over HTTP completes immediately, rather than waiting up to the specified
                    // time or when a cancellation token is signaled, so if we want it to poll at the same rate, we need
                    // to add an explicit delay here.
                    await Task.Delay(SleepDuration, cancellationToken);
                }

                _logger.LogInformation($"Device waiting for C2D messages from the hub for {SleepDuration}." +
                    $"\nUse the IoT Hub Azure Portal or Azure IoT Explorer to send a message to this device.");

                await ReceiveMessageAndCompleteAsync(cancellationToken);
            }
        }

        private async Task ReceiveMessageAndCompleteAsync(CancellationToken cancellationToken)
        {
            Message receivedMessage = null;
            try
            {
                receivedMessage = await _deviceClient.ReceiveAsync(cancellationToken);
            }
            catch (IotHubCommunicationException ex) when (ex.InnerException is OperationCanceledException)
            {
                _logger.LogInformation("Timed out waiting to receive a message.");
            }

            if (receivedMessage == null)
            {
                _logger.LogInformation("No message received.");
                return;
            }

            using (receivedMessage)
            {
                string messageData = Encoding.ASCII.GetString(receivedMessage.GetBytes());
                var formattedMessage = new StringBuilder($"Received message '{receivedMessage.MessageId}': [{messageData}]");

                foreach (KeyValuePair<string, string> prop in receivedMessage.Properties)
                {
                    formattedMessage.AppendLine($"\n\tProperty: key={prop.Key}, value={prop.Value}");
                }
                _logger.LogInformation(formattedMessage.ToString());

                try
                {
                    await _deviceClient.CompleteAsync(receivedMessage, cancellationToken);
                    _logger.LogInformation($"Completed message '{receivedMessage.MessageId}'.");
                }
                catch (DeviceMessageLockLostException)
                {
                    _logger.LogWarning($"Took too long to process and complete a C2D message; it will be redelivered.");
                }
            }
        }

        private Message PrepareMessage(int messageId)
        {
            const int temperatureThreshold = 30;

            int temperature = RandomGenerator.Next(20, 35);
            int humidity = RandomGenerator.Next(60, 80);
            string messagePayload = $"{{\"temperature\":{temperature},\"humidity\":{humidity}}}";

            var eventMessage = new Message(Encoding.UTF8.GetBytes(messagePayload))
            {
                MessageId = messageId.ToString(),
                ContentEncoding = Encoding.UTF8.ToString(),
                ContentType = "application/json",
            };
            eventMessage.Properties.Add("temperatureAlert", (temperature > temperatureThreshold) ? "true" : "false");

            if (temperature > temperatureThreshold)
            {
                _logger.LogInformation("Sending message with level critical");
                eventMessage.Properties.Add("level", "critical");
            }
            else
            {
                _logger.LogInformation("Sending message with level normal");
                eventMessage.Properties.Add("level", "normal");
            }

            return eventMessage;
        }

        // If the client reports Connected status, it is already in operational state.
        // If the client reports Disconnected_retrying status, it is trying to recover its connection.
        // If the client reports Disconnected status, you will need to dispose and recreate the client.
        // If the client reports Disabled status, you will need to dispose and recreate the client.
        private bool ShouldClientBeInitialized(ConnectionStatus connectionStatus)
        {
            return (connectionStatus == ConnectionStatus.Disconnected || connectionStatus == ConnectionStatus.Disabled)
                && _deviceConnectionStrings.Any();
        }
    }
}
