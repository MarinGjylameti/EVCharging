namespace EVCharging.Infrastructure.Charging.Ocpp;

public sealed class OcppOptions
{
    // These subordinate options are effective only when Features:TestMode is on.
    public bool SimulatorEnabled { get; set; } = true;
    public bool AllowAnonymousSimulators { get; set; } = true;
    public bool TestMode { get; internal set; }
    public static string[] DefaultSimulatorIdentities =>
        Enumerable.Range(1, 10).Select(number => $"SIM-{number:000}").ToArray();

    public string[] SimulatorIdentities { get; set; } = DefaultSimulatorIdentities;
    public string SimulatorPassword { get; set; } = "";
    public string SimulatorTagId { get; set; } = "TAG-001";
    public int HeartbeatSeconds { get; set; } = 30;
    public int OfflineSeconds { get; set; } = 120;
}
