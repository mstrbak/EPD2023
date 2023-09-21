using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Provisioning.Client;
using Microsoft.Azure.Devices.Provisioning.Client.Transport;
using Microsoft.Azure.Devices.Shared;

namespace ProvisioningDemo
{
    internal static class Utils
    {
        internal static ProvisioningTransportHandler CreateTransportHandler(TransportType transport)
        {
            switch (transport)
            {
                case TransportType.Amqp: return new ProvisioningTransportHandlerAmqp();
                case TransportType.Amqp_Tcp_Only: return new ProvisioningTransportHandlerAmqp(TransportFallbackType.TcpOnly);
                case TransportType.Amqp_WebSocket_Only: return new ProvisioningTransportHandlerAmqp(TransportFallbackType.WebSocketOnly);
                case TransportType.Mqtt: return new ProvisioningTransportHandlerMqtt();
                case TransportType.Mqtt_Tcp_Only: return new ProvisioningTransportHandlerMqtt(TransportFallbackType.TcpOnly);
                case TransportType.Mqtt_WebSocket_Only: return new ProvisioningTransportHandlerMqtt(TransportFallbackType.WebSocketOnly);
                case TransportType.Http1: return new ProvisioningTransportHandlerHttp();
                default: throw new ArgumentOutOfRangeException(nameof(transport));
            }
        }

        internal static string DumpRegistration(DeviceRegistrationResult registration)
        {
            string registrationInfo = $"AssignedHub: {registration.AssignedHub}{Environment.NewLine}" +
                                      $"CreatedDateTimeUtc : {registration.CreatedDateTimeUtc}{Environment.NewLine}" +
                                      $"DeviceId: {registration.DeviceId}{Environment.NewLine}" +
                                      $"ErrorCode: {registration.ErrorCode}{Environment.NewLine}" +
                                      $"ErrorMessage: {registration.ErrorMessage}{Environment.NewLine}" +
                                      $"Etag: {registration.Etag}{Environment.NewLine}" +
                                      $"GenerationId : {registration.GenerationId}{Environment.NewLine}" +
                                      $"JsonPayload : {registration.JsonPayload}{Environment.NewLine}" +
                                      $"LastUpdatedDateTimeUtc : {registration.LastUpdatedDateTimeUtc}{Environment.NewLine}" +
                                      $"RegistrationId  : {registration.RegistrationId}{Environment.NewLine}" +
                                      $"Status : {registration.Status}{Environment.NewLine}" +
                                      $"Substatus : {registration.Substatus}{Environment.NewLine}";

            return registrationInfo;
        }
    }
}
