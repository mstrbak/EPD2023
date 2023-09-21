using Microsoft.Azure.Devices.Client;

namespace ProvisioningDemo
{
    public static class Settings
    {
        public static string SymmetricKeyPrimary { get; set; } = "D46tbbLMY2pp/XV8a6LAaK4FsDQZZpOemzM7koxXtxmtyGZpN6F5S0ApXVxdGx1GP0sDioUK4pqTCTT0XjyHDg==";
        public static string SymmetricKeySecondary { get; set; } = "/DD0yqD60LaLX7EwKTxrim2T7TtYiRfuD6tLyiDO1yhC4++s7VH/l2fswnFuPBga81ziXAmtcVtK1PzK78496w==";

        public static string GlobalDeviceEndpoint { get; set; } = "global.azure-devices-provisioning.net";

        public static string IdScope { get; set; } = "0ne00B0BC44";
        public static TransportType TransportType { get; set; } = TransportType.Amqp;
    }
}
