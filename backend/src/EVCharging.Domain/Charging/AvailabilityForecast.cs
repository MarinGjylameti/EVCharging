namespace EVCharging.Domain.Charging;

public sealed record AvailabilityForecast(
    int ConnectorId,
    DateTimeOffset EstimatedAvailableAt,
    DateTimeOffset ObservedAt,
    string Source,
    DateTimeOffset? ValidUntil = null
);

public static class AvailabilityForecastRules
{
    private static readonly TimeSpan MaxDeviceAge = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan MaxDemoAge = TimeSpan.FromHours(1);
    private static readonly TimeSpan MaxHorizon = TimeSpan.FromHours(24);

    public static void SetDeviceEstimate(
        ChargingStation station,
        int connectorId,
        DateTimeOffset estimatedAvailableAt,
        DateTimeOffset observedAt
    )
    {
        if (
            station.Status != PointState.Charging
            || connectorId < 1
            || connectorId > station.ConnectorCount
            || estimatedAvailableAt <= observedAt
            || estimatedAvailableAt > observedAt.Add(MaxHorizon)
        )
            throw new ArgumentException("Invalid charging availability estimate.");
        station.ForecastConnectorId = connectorId;
        station.ForecastAvailableAt = estimatedAvailableAt.ToUniversalTime();
        station.ForecastObservedAt = observedAt.ToUniversalTime();
        station.ForecastSource = "Device";
        station.ForecastValidUntil = observedAt.Add(MaxDeviceAge);
    }

    public static AvailabilityForecast? ForDriver(ChargingStation station, DateTimeOffset now)
    {
        if (
            station.IsArchived
            || station.Status != PointState.Charging
            || station.ForecastConnectorId is not int connectorId
            || connectorId < 1
            || connectorId > station.ConnectorCount
            || station.ForecastAvailableAt is not DateTimeOffset end
            || station.ForecastObservedAt is not DateTimeOffset observed
            || station.ForecastSource is not ("Device" or "Demo" or "Calculated" or "Simulator")
            || station.ForecastSource is ("Calculated" or "Simulator")
                && station.ForecastValidUntil is null
            || station.ForecastValidUntil <= now
            || observed > now.AddMinutes(1)
            || now - observed > (station.ForecastSource == "Demo" ? MaxDemoAge : MaxDeviceAge)
            || end <= now
            || end > observed.Add(MaxHorizon)
        )
            return null;
        return new AvailabilityForecast(
            connectorId,
            end,
            observed,
            station.ForecastSource,
            station.ForecastValidUntil
        );
    }

    public static void Clear(ChargingStation station)
    {
        station.ForecastConnectorId = null;
        station.ForecastAvailableAt = null;
        station.ForecastObservedAt = null;
        station.ForecastSource = null;
        station.ForecastValidUntil = null;
    }

    public static AvailabilityForecast? Calculate(
        ChargingSession session,
        ChargingConnector connector,
        DateTimeOffset now
    )
    {
        if (
            session.EndedAt is not null
            || session.Status != ChargingState.Charging
            || !connector.IsOnline
            || connector.Status != ChargingState.Charging
            || connector.FaultCode is not (null or "NoError")
            || connector.DeviceFaultCode is not (null or "NoError")
            || session.BatteryCapacityKWh is not (> 0 and <= 1000)
            || session.StateOfCharge is not (>= 0 and < 100)
            || session.PowerKW is not (> 0 and <= 100_000)
            || session.PowerScope != "Total"
            || session.PowerReceivedAt is not { } powerAt
            || powerAt > now
            || session.SocReceivedAt is not { } socAt
            || socAt > now
        )
            return null;
        var powerUntil = powerAt.AddSeconds(120);
        var socUntil = socAt.AddMinutes(5);
        var validUntil = powerUntil < socUntil ? powerUntil : socUntil;
        if (validUntil <= now)
            return null;
        var seconds =
            session.BatteryCapacityKWh.Value
            * (100 - session.StateOfCharge.Value)
            / 100
            / session.PowerKW.Value
            * 3600;
        if (seconds <= 0 || seconds > (decimal)MaxHorizon.TotalSeconds)
            return null;
        return new(
            session.ConnectorNumber,
            now.AddSeconds((double)seconds),
            now,
            "Calculated",
            validUntil
        );
    }
}
