using EVCharging.Domain.Charging;

namespace EVCharging.Tests;

public sealed class AvailabilityForecastTests
{
    [Fact]
    public void CalculatedForecastUsesUsableCapacityAndExpiresWithTheOldestRequiredReading()
    {
        var now = DateTimeOffset.UtcNow;
        var session = new ChargingSession
        {
            ConnectorNumber = 1,
            Status = ChargingState.Charging,
            BatteryCapacityKWh = 77,
            StateOfCharge = 50,
            PowerKW = 22,
            PowerScope = "Total",
            PowerReceivedAt = now,
            SocReceivedAt = now.AddMinutes(-4),
        };
        var connector = new ChargingConnector
        {
            Number = 1,
            Status = ChargingState.Charging,
            IsOnline = true,
        };
        var forecast = AvailabilityForecastRules.Calculate(session, connector, now)!;
        Assert.Equal(105, (forecast.EstimatedAvailableAt - now).TotalMinutes, 6);
        Assert.Equal(now.AddMinutes(1), forecast.ValidUntil);
        Assert.Null(AvailabilityForecastRules.Calculate(session, connector, now.AddMinutes(1)));
        session.SocReceivedAt = now;
        Assert.Equal(
            now.AddMinutes(2),
            AvailabilityForecastRules.Calculate(session, connector, now)!.ValidUntil
        );
        session.PowerScope = "L1";
        Assert.Null(AvailabilityForecastRules.Calculate(session, connector, now));
        session.PowerScope = "Total";
        connector.IsOnline = false;
        Assert.Null(AvailabilityForecastRules.Calculate(session, connector, now));
        connector.IsOnline = true;
        connector.FaultCode = "HighTemperature";
        Assert.Null(AvailabilityForecastRules.Calculate(session, connector, now));
    }

    [Fact]
    public void PublishesOnlyFreshForecastsForAnActiveConnector()
    {
        var now = DateTimeOffset.UtcNow;
        var station = new ChargingStation { ConnectorCount = 2, Status = PointState.Charging };
        AvailabilityForecastRules.SetDeviceEstimate(station, 1, now.AddMinutes(40), now);

        var forecast = AvailabilityForecastRules.ForDriver(station, now);
        Assert.NotNull(forecast);
        Assert.Equal(1, forecast.ConnectorId);
        Assert.Equal("Device", forecast.Source);
        Assert.Null(AvailabilityForecastRules.ForDriver(station, now.AddMinutes(16)));

        station.Status = PointState.Available;
        Assert.Null(AvailabilityForecastRules.ForDriver(station, now));
        AvailabilityForecastRules.Clear(station);
        Assert.Null(station.ForecastAvailableAt);
    }

    [Fact]
    public void RejectsInvalidConnectorAndUnboundedCompletionTime()
    {
        var now = DateTimeOffset.UtcNow;
        var station = new ChargingStation { ConnectorCount = 2, Status = PointState.Charging };
        Assert.Throws<ArgumentException>(() =>
            AvailabilityForecastRules.SetDeviceEstimate(station, 3, now.AddMinutes(40), now)
        );
        Assert.Throws<ArgumentException>(() =>
            AvailabilityForecastRules.SetDeviceEstimate(station, 1, now.AddHours(25), now)
        );
        Assert.Throws<ArgumentException>(() =>
            AvailabilityForecastRules.SetDeviceEstimate(station, 1, now.AddMinutes(-1), now)
        );
    }
}
