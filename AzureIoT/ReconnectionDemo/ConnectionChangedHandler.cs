//using Microsoft.Azure.Devices.Client;
//using Microsoft.Azure.Devices.Client.Exceptions;
//using Microsoft.Azure.Devices.Shared;
//using Microsoft.Extensions.Logging;
//using System.Text;

//namespace ReconnectionDemo
//{
//    internal class ConnectionChangedHandler
//    {
//        private readonly ILogger _logger;
//        private readonly Func<CancellationToken, Task> _getTwinChanges;

//        public ConnectionChangedHandler(ILogger logger, Func<CancellationToken, Task> getTwinChanges)
//        {
//            _logger = logger;
//            _getTwinChanges = getTwinChanges;
//        }

//        // It is not generally a good practice to have async void methods, however, DeviceClient.ConnectionStatusChangeHandlerAsync() event handler signature
//        // has a void return type. As a result, any operation within this block will be executed unmonitored on another thread.
//        // To prevent multi-threaded synchronization issues, the async method InitializeClientAsync being called in here first grabs a lock before attempting to
//        // initialize or dispose the device client instance; the async method GetTwinAndDetectChangesAsync is implemented similarly for the same purpose.
//        internal async void Handle(ConnectionStatus status, ConnectionStatusChangeReason reason)
//        {
//            _logger.LogDebug($"Connection status changed: status={status}, reason={reason}");
//            _connectionStatus = status;

//            switch (status)
//            {
//                case ConnectionStatus.Connected:
//                    _logger.LogDebug("### The DeviceClient is CONNECTED; all operations will be carried out as normal.");

//                    // Call GetTwinAndDetectChangesAsync() to retrieve twin values from the server once the connection status changes into Connected.
//                    // This can get back "lost" twin updates in a device reconnection from status like Disconnected_Retrying or Disconnected.
//                    //
//                    // Howevever, considering how a fleet of devices connected to a hub may behave together, one must consider the implication of performing
//                    // work on a device (e.g., get twin) when it comes online. If all the devices go offline and then come online at the same time (for example,
//                    // during a servicing event) it could introduce increased latency or even throttling responses.
//                    // For more information, see https://docs.microsoft.com/azure/iot-hub/iot-hub-devguide-quotas-throttling#traffic-shaping.
//                    await _getTwinChanges(_appCancellation.Token);
//                    _logger.LogDebug("The client has retrieved twin values after the connection status changes into CONNECTED.");
//                    break;

//                case ConnectionStatus.Disconnected_Retrying:
//                    _logger.LogDebug("### The DeviceClient is retrying based on the retry policy. Do NOT close or open the DeviceClient instance.");
//                    break;

//                case ConnectionStatus.Disabled:
//                    _logger.LogDebug("### The DeviceClient has been closed gracefully." +
//                        "\nIf you want to perform more operations on the device client, you should dispose (DisposeAsync()) and then open (OpenAsync()) the client.");
//                    break;

//                case ConnectionStatus.Disconnected:
//                    var waitingTime = 5000;
//                    _logger.LogDebug($"Waiting {waitingTime}ms before trying to reconnect.");
//                    await Task.Delay(waitingTime);

//                    switch (reason)
//                    {
//                        case ConnectionStatusChangeReason.Bad_Credential:
//                            // When getting this reason, the current connection string being used is not valid.
//                            // If we had a backup, we can try using that.
//                            _deviceConnectionStrings.RemoveAt(0);
//                            if (_deviceConnectionStrings.Any())
//                            {
//                                _logger.LogWarning($"The current connection string is invalid. Trying another.");

//                                try
//                                {
//                                    await InitializeAndSetupClientAsync(_appCancellation.Token);
//                                }
//                                catch (OperationCanceledException) { } // User canceled

//                                break;
//                            }

//                            _logger.LogWarning("### The supplied credentials are invalid. Update the parameters and run again.");
//                            _appCancellation.Cancel();
//                            break;

//                        case ConnectionStatusChangeReason.Device_Disabled:
//                            _logger.LogWarning("### The device has been deleted or marked as disabled (on your hub instance)." +
//                                "\nFix the device status in Azure and then create a new device client instance.");
//                            //_appCancellation.Cancel();

//                            try
//                            {
//                                await InitializeAndSetupClientAsync(_appCancellation.Token);
//                            }
//                            catch (OperationCanceledException) { } // User canceled

//                            break;

//                        case ConnectionStatusChangeReason.Retry_Expired:
//                            _logger.LogWarning("### The DeviceClient has been disconnected because the retry policy expired." +
//                                "\nIf you want to perform more operations on the device client, you should dispose (DisposeAsync()) and then open (OpenAsync()) the client.");

//                            try
//                            {
//                                await InitializeAndSetupClientAsync(_appCancellation.Token);
//                            }
//                            catch (OperationCanceledException) { } // User canceled

//                            break;

//                        case ConnectionStatusChangeReason.Communication_Error:
//                            _logger.LogWarning("### The DeviceClient has been disconnected due to a non-retry-able exception. Inspect the exception for details." +
//                                "\nIf you want to perform more operations on the device client, you should dispose (DisposeAsync()) and then open (OpenAsync()) the client.");

//                            try
//                            {
//                                await InitializeAndSetupClientAsync(_appCancellation.Token);
//                            }
//                            catch (OperationCanceledException) { } // User canceled

//                            break;

//                        default:
//                            _logger.LogError("### This combination of ConnectionStatus and ConnectionStatusChangeReason is not expected, contact the client library team with logs.");
//                            break;
//                    }

//                    break;

//                default:
//                    _logger.LogError("### This combination of ConnectionStatus and ConnectionStatusChangeReason is not expected, contact the client library team with logs.");
//                    break;
//            }
//        }
//    }
//}
