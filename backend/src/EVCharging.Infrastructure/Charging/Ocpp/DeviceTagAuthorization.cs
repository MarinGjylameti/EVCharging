using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EVCharging.Infrastructure.Persistence;

namespace EVCharging.Infrastructure.Charging.Ocpp;

internal static class DeviceTagAuthorization
{
    public static string Hash(string tag) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tag)));

    public static string[] Hashes(OcppChargePoint device) =>
        string.IsNullOrEmpty(device.AuthorizedTagHashesJson)
            ? []
            : JsonSerializer.Deserialize<string[]>(device.AuthorizedTagHashesJson) ?? [];

    public static bool Accepted(OcppChargePoint device, string tag, OcppOptions options) =>
        device.ProvisionedSerialNumber is not null
            ? device.AssignedDriverId is not null
                && (
                    options.TestMode && device.DriverCardIdTag is null
                    || device.DriverCardIdTag == tag
                )
                || Hashes(device).Contains(Hash(tag), StringComparer.Ordinal)
            : options.SimulatorEnabled
                && options.SimulatorIdentities.Contains(device.Identity, StringComparer.Ordinal)
                && tag == options.SimulatorTagId;
}
