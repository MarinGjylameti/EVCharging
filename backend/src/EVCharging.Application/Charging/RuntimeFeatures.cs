namespace EVCharging.Application.Charging;

// One server-owned switch for demonstrations and end-user diagnostic tools.
// Physical charger connectivity and operational administration remain available.
public sealed class RuntimeFeatures
{
    public bool TestMode { get; set; }
}
