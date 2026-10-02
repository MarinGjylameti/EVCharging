using System.Text.Json;
using EVCharging.Application.Charging;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EVCharging.Infrastructure.Charging.Ocpp;

public sealed class OcppMeterHandler(
    ChargingDbContext db,
    TimeProvider clock,
    IOptions<OcppOptions> options,
    IChargerEventReceiver receiver
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
        else if (number > 0)
            transaction = await db.OcppChargingTransactions.SingleOrDefaultAsync(
                x =>
                    x.ChargePointId == deviceId
                    && x.ConnectorNumber == number
                    && x.StoppedAt == null,
                ct
            );
        var simulation =
            transaction is not null
            && options.Value.SimulatorEnabled
            && await db.Stations.AnyAsync(x => x.Id == transaction.StationId && x.IsDemo, ct);
        await AddSamplesAsync(deviceId, message.Id, number, transaction, samples, simulation, ct);
    }

    internal async Task AddSamplesAsync(
        Guid deviceId,
        Guid messageId,
        int number,
        OcppChargingTransaction? transaction,
        IReadOnlyList<ParsedSample> samples,
        bool simulation,
        CancellationToken ct
    )
    {
        var now = clock.GetUtcNow();
        var records = samples
            .Select(sample => new OcppMeterSample
            {
                ChargePointId = deviceId,
                MessageId = messageId,
                TransactionId = transaction?.Id,
                ConnectorNumber = number,
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
            })
            .ToArray();
        db.OcppMeterSamples.AddRange(records);
        if (
            transaction is null
            || transaction.StoppedAt is not null
            || transaction.SessionId is null
        )
            return;
        var connector = await db.OcppConnectors.SingleAsync(
            x => x.ChargePointId == deviceId && x.ConnectorNumber == number,
            ct
        );
        // Normalize only this incoming frame. Live queries never scan raw history.
        foreach (
            var group in records
                .Where(x =>
                    x.ObservedAt >= transaction.StartedAt
                    && x.ObservedAt <= (simulation ? now.AddDays(1) : now.AddMinutes(5))
                )
                .OrderBy(x => x.ObservedAt)
                .GroupBy(x => x.ObservedAt)
        )
        {
            var readings = OcppMeterReading.FromSamples(
                group,
                transaction.StartedAt,
                now,
                simulation
            );
            var energy = readings.LastOrDefault(x =>
                x.Measurand == "Energy.Active.Import.Register"
            );
            var power = readings.LastOrDefault(x => x.Measurand == "Power.Active.Import");
            var soc = OcppMeterReading.StateOfCharge(group, simulation);
            var temperature = group
                .FirstOrDefault(x =>
                    x.Measurand == "Temperature"
                    && x.Phase == null
                    && x.Format != "SignedData"
                    && x.Unit is null or "Celsius"
                )
                ?.NumericValue;
            if (energy is null && power is null && soc is null && temperature is null)
                continue;
            await receiver.ReceiveAsync(
                new(
                    "OCPP",
                    messageId.ToString(),
                    deviceId.ToString(),
                    number.ToString(),
                    transaction.Ocpp16TransactionId.ToString(),
                    ChargerEventKind.Measurement,
                    group.Key,
                    energy?.SessionEnergy(transaction.MeterStartWh),
                    power?.Value,
                    null,
                    transaction.SessionId,
                    connector.BusinessConnectorId,
                    now,
                    StateOfCharge: soc,
                    TemperatureCelsius: temperature,
                    EnergyScope: energy?.Scope,
                    PowerScope: power?.Scope
                ),
                ct
            );
        }
    }
}
