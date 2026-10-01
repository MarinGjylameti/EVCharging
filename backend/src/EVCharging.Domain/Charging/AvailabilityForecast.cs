namespace EVCharging.Domain.Charging;

public sealed record AvailabilityForecast(
    int ConnectorId, DateTimeOffset EstimatedAvailableAt, DateTimeOffset ObservedAt, string Source);

public static class AvailabilityForecastRules
{
    private static readonly TimeSpan MaxDeviceAge = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan MaxDemoAge = TimeSpan.FromHours(1);
    private static readonly TimeSpan MaxHorizon = TimeSpan.FromHours(24);

    public static void SetDeviceEstimate(ChargingStation station, int connectorId,
        DateTimeOffset estimatedAvailableAt, DateTimeOffset observedAt)
    {
        if (station.Status != PointState.Charging || connectorId < 1 || connectorId > station.ConnectorCount ||
            estimatedAvailableAt <= observedAt || estimatedAvailableAt > observedAt.Add(MaxHorizon))
            throw new ArgumentException("Invalid charging availability estimate.");
        station.ForecastConnectorId = connectorId;
        station.ForecastAvailableAt = estimatedAvailableAt.ToUniversalTime();
        station.ForecastObservedAt = observedAt.ToUniversalTime();
        station.ForecastSource = "Device";
    }

    public static AvailabilityForecast? ForDriver(ChargingStation station, DateTimeOffset now)
    {
        if (station.IsArchived || station.Status != PointState.Charging ||
            station.ForecastConnectorId is not int connectorId || connectorId < 1 || connectorId > station.ConnectorCount ||
            station.ForecastAvailableAt is not DateTimeOffset end ||
            station.ForecastObservedAt is not DateTimeOffset observed ||
            station.ForecastSource is not ("Device" or "Demo") ||
            observed > now.AddMinutes(1) || now - observed >
                (station.ForecastSource == "Demo" ? MaxDemoAge : MaxDeviceAge) ||
            end <= now || end > observed.Add(MaxHorizon)) return null;
        return new AvailabilityForecast(connectorId, end, observed, station.ForecastSource);
    }

    public static void Clear(ChargingStation station)
    {
        station.ForecastConnectorId = null;
        station.ForecastAvailableAt = null;
        station.ForecastObservedAt = null;
        station.ForecastSource = null;
    }
}
