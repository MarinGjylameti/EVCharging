using EVCharging.Domain.Charging;
using EVCharging.Infrastructure.Charging;

namespace EVCharging.Tests;

public class OcppTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-28T17:00:00Z");

    [Fact]
    public void StartUsesWhAndKeepsIdTagUnverified()
    {
        var result = Assert.Single(Ocpp16TelemetryParser.ParseCall("CP-1",
            """[2,"start-1","StartTransaction",{"connectorId":1,"idTag":"DRIVER-TAG","meterStart":123000,"timestamp":"2026-09-28T16:59:00Z"}]""", Now));
        Assert.Equal(123m, result.MeterRegisterKWh);
        Assert.Equal("DRIVER-TAG", result.UnverifiedIdTag);
        Assert.Null(result.TransactionId); // The future CSMS must allocate this durably.
    }

    [Fact]
    public void MeterNormalizesWhAndWWithoutAddingPhaseDuplicates()
    {
        var result = Assert.Single(Ocpp16TelemetryParser.ParseCall("CP-1",
            """[2,"meter-1","MeterValues",{"connectorId":1,"transactionId":2,"meterValue":[{"timestamp":"2026-09-28T17:00:00Z","sampledValue":[{"value":"143000"},{"value":"7200","measurand":"Power.Active.Import","unit":"W"},{"value":"50000","phase":"L1"}]}]}]""", Now));
        Assert.Equal(143m, result.MeterRegisterKWh);
        Assert.Equal(7.2m, result.PowerKW);
        Assert.Equal(20m, result.MeterRegisterKWh - 123m);
    }

    [Fact]
    public void StopRequiresTransactionMappingRatherThanInventingConnector()
    {
        var result = Assert.Single(Ocpp16TelemetryParser.ParseCall("CP-1",
            """[2,"stop-1","StopTransaction",{"transactionId":2,"meterStop":143000,"timestamp":"2026-09-28T17:00:00Z"}]""", Now));
        Assert.Null(result.ConnectorId);
        Assert.Equal(2, result.TransactionId);
        Assert.Equal(143m, result.MeterRegisterKWh);
    }

    [Theory]
    [InlineData("Preparing", PointState.Occupied)]
    [InlineData("SuspendedEVSE", PointState.Occupied)]
    [InlineData("Reserved", PointState.Occupied)]
    [InlineData("Unavailable", PointState.OutOfService)]
    [InlineData("Charging", PointState.Charging)]
    public void StatusIsNormalized(string value, PointState expected) => Assert.Equal(expected, Ocpp16TelemetryParser.Status(value));

    [Theory]
    [InlineData("""[2,"x","MeterValues",{"connectorId":1,"meterValue":[{"timestamp":"2026-09-28T17:00:00Z","sampledValue":[{"value":"NaN"}]}]}]""")]
    [InlineData("""[2,"x","StartTransaction",{"connectorId":0,"idTag":"A","meterStart":0,"timestamp":"2026-09-28T17:00:00Z"}]""")]
    [InlineData("""[2,"x","StartTransaction",{"connectorId":1,"idTag":"A","meterStart":0,"timestamp":"2026-09-28T17:00:00"}]""")]
    [InlineData("""[2,"x","RemoteStartTransaction",{"idTag":"A"}]""")]
    public void MalformedAndOutOfScopeMessagesAreRejected(string frame) =>
        Assert.Throws<OcppPayloadException>(() => Ocpp16TelemetryParser.ParseCall("CP-1", frame, Now));
}
