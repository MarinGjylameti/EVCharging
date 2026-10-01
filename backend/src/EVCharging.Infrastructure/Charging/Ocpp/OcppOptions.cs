namespace EVCharging.Infrastructure.Charging.Ocpp;

public sealed class OcppOptions
{
    public bool SimulatorEnabled { get; set; }
    public bool AllowAnonymousSimulators { get; set; }
    public string[] SimulatorIdentities { get; set; } = ["SIM-001", "SIM-002", "SIM-003"];
    public string SimulatorPassword { get; set; } = "";
    public string SimulatorTagId { get; set; } = "TAG-001";
    public int HeartbeatSeconds { get; set; } = 30;
    public int OfflineSeconds { get; set; } = 120;
}
