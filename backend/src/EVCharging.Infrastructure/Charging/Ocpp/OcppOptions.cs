namespace EVCharging.Infrastructure.Charging.Ocpp;

public sealed class OcppOptions
{
    public bool SimulatorEnabled { get; set; }
    public bool AllowAnonymousSimulators { get; set; }
    public static string[] DefaultSimulatorIdentities =>
        Enumerable.Range(1, 10).Select(number => $"SIM-{number:000}").ToArray();

    public string[] SimulatorIdentities { get; set; } = DefaultSimulatorIdentities;
    public string SimulatorPassword { get; set; } = "";
    public string SimulatorTagId { get; set; } = "TAG-001";
    public int HeartbeatSeconds { get; set; } = 30;
    public int OfflineSeconds { get; set; } = 120;
}
