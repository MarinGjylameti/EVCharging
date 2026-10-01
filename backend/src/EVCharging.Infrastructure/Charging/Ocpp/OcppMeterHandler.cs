using System.Text.Json;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EVCharging.Infrastructure.Charging.Ocpp;

public sealed class OcppMeterHandler(ChargingDbContext db, TimeProvider clock)
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
        AddSamples(deviceId, message.Id, number, transaction, samples);
        if (transaction is not null)
            OcppTransactionHandler.StageSessionChange(db, transaction, clock.GetUtcNow());
    }

    internal void AddSamples(
        Guid deviceId,
        Guid messageId,
        int connector,
        OcppChargingTransaction? transaction,
        IReadOnlyList<ParsedSample> samples
    )
    {
        foreach (var sample in samples)
        {
            db.OcppMeterSamples.Add(
                new OcppMeterSample
                {
                    ChargePointId = deviceId,
                    MessageId = messageId,
                    TransactionId = transaction?.Id,
                    ConnectorNumber = connector,
                    MeterValueIndex = sample.MeterIndex,
                    SampleIndex = sample.SampleIndex,
                    ObservedAt = sample.Timestamp,
                    ReceivedAt = clock.GetUtcNow(),
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
            if (
                transaction is null
                || transaction.StoppedAt is not null
                || sample.Phase is not null
                || sample.Format == "SignedData"
                || sample.Timestamp < transaction.StartedAt
                || sample.Timestamp > clock.GetUtcNow().AddMinutes(5)
                || sample.Timestamp < transaction.LastMeasurementAt
                || sample.Numeric is not { } value
                || value < 0
            )
                continue;
            if (
                sample.Measurand == "Energy.Active.Import.Register"
                && sample.Unit is null or "Wh" or "kWh"
            )
            {
                var wh = sample.Unit == "kWh" ? value * 1000 : value;
                var energy = (wh - transaction.MeterStartWh) / 1000m;
                if (energy >= (transaction.EnergyKWh ?? 0) && energy < 1_000_000_000_000m)
                    transaction.EnergyKWh = decimal.Round(energy, 6);
            }
            if (sample.Measurand == "Power.Active.Import" && sample.Unit is "W" or "kW")
            {
                var power = sample.Unit == "W" ? value / 1000 : value;
                if (power < 999_999_999m)
                    transaction.CurrentPowerKW = decimal.Round(power, 3);
            }
            transaction.LastMeasurementAt = sample.Timestamp;
        }
    }
}
