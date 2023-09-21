using Microsoft.Azure.Devices.Client;

namespace ReconnectionDemo
{
    public class Settings
    {
        public string DeviceName { get; set; } = string.Empty;

        public string DeviceConnectionStringPrimary { get; set; } = string.Empty;
        public string DeviceConnectionStringSecondary { get; set; } = string.Empty;

        public int ApplicationRunningTime { get; set; } = -1;

        public TransportType TransportType { get; set; } = TransportType.Amqp;
    }
}
