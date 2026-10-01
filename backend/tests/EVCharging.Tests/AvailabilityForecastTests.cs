using EVCharging.Domain.Charging;

namespace EVCharging.Tests;

public sealed class AvailabilityForecastTests
{
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
