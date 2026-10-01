using System.Text.Json;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EVCharging.Infrastructure.Charging.Ocpp;

public sealed class OcppMeterHandler(
    ChargingDbContext db,
    TimeProvider clock,
    IOptions<OcppOptions> options
)
{
    public async Task RecordAsync(
        Guid deviceId,
        OcppInboundMessage message,
        JsonElement payload,
        CancellationToken ct
    )
    {
        var number = OcppPayload.Integer(payload, "connectorId");
        var samples = OcppPayload.Samples(payload, "meterValue");
        if (samples.Count == 0)
            throw new OcppFault("FormationViolation", "Missing meter values.");
        if (
            number > 0
            && !await db.OcppConnectors.AnyAsync(
                x => x.ChargePointId == deviceId && x.ConnectorNumber == number,
                ct
            )
        )
            throw new OcppFault("PropertyConstraintViolation", "Connector is not provisioned.");
        OcppChargingTransaction? transaction = null;
        if (payload.TryGetProperty("transactionId", out _))
        {
            var id = OcppPayload.Integer(payload, "transactionId", 1);
            transaction =
                await db.OcppChargingTransactions.SingleOrDefaultAsync(
                    x =>
                        x.ChargePointId == deviceId
                        && x.Ocpp16TransactionId == id
                        && x.ConnectorNumber == number,
                    ct
                )
                ?? throw new OcppFault(
                    "PropertyConstraintViolation",
                    "Unknown transaction for this connector."
                );
        }
        var simulationClock =
            transaction is not null
            && options.Value.SimulatorEnabled
            && await db.Stations.AnyAsync(x => x.Id == transaction.StationId && x.IsDemo, ct);
        AddSamples(deviceId, message.Id, number, transaction, samples, simulationClock);
        if (transaction is not null)
            OcppTransactionHandler.StageSessionChange(db, transaction, clock.GetUtcNow());
    }

    internal void AddSamples(
        Guid deviceId,
        Guid messageId,
        int connector,
        OcppChargingTransaction? transaction,
        IReadOnlyList<ParsedSample> samples,
        bool simulationClock = false
    )
    {
        var now = clock.GetUtcNow();
        var records = new List<OcppMeterSample>();
        foreach (var sample in samples)
        {
            records.Add(
                new OcppMeterSample
                {
                    ChargePointId = deviceId,
                    MessageId = messageId,
                    TransactionId = transaction?.Id,
                    ConnectorNumber = connector,
                    MeterValueIndex = sample.MeterIndex,
                    SampleIndex = sample.SampleIndex,
                    ObservedAt = sample.Timestamp,
                    ReceivedAt = now,
                    ValueText = sample.Text,
                    NumericValue = sample.Numeric,
                    Measurand = sample.Measurand,
                    Unit = sample.Unit,
                    Phase = sample.Phase,
                    Location = sample.Location,
                    Context = sample.Context,
                    Format = sample.Format,
                }
            );
        }
        db.OcppMeterSamples.AddRange(records);
        if (transaction is null || transaction.StoppedAt is not null)
            return;
        foreach (
            var reading in OcppMeterReading.FromSamples(
                records,
                transaction.StartedAt,
                now,
                simulationClock
            )
        )
        {
            if (reading.ObservedAt < transaction.LastMeasurementAt)
                continue;
            if (
                reading.SessionEnergy(transaction.MeterStartWh) is { } energy
                && energy >= (transaction.EnergyKWh ?? 0)
            )
                transaction.EnergyKWh = energy;
            if (reading.Measurand == "Power.Active.Import")
                transaction.CurrentPowerKW = decimal.Round(reading.Value, 3);
            transaction.LastMeasurementAt = reading.ObservedAt;
        }
    }
}
