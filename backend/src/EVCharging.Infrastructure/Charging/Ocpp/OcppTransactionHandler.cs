using System.Text.Json;
using EVCharging.Application.Charging;
using EVCharging.Domain.Charging;
using EVCharging.Domain.Identity;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace EVCharging.Infrastructure.Charging.Ocpp;

public sealed class OcppTransactionHandler(
    ChargingDbContext db,
    OcppStationProjection projection,
    OcppMeterHandler meters,
    TimeProvider clock,
    IOptions<OcppOptions> options,
    IConfiguration configuration
)
{
    public async Task<object> StartAsync(
        OcppChargePoint device,
        OcppInboundMessage message,
        JsonElement payload,
        CancellationToken ct
    )
    {
        var number = OcppPayload.Integer(payload, "connectorId", 1);
        var idTag = OcppPayload.Text(payload, "idTag", 20);
        var meter = OcppPayload.Integer(payload, "meterStart");
        var started = OcppPayload.Timestamp(payload);
        if (idTag != options.Value.SimulatorTagId)
            throw new OcppFault(
                "SecurityError",
                "The authorization tag is not registered for this simulator."
            );
        var connector =
            await db.OcppConnectors.SingleOrDefaultAsync(
                x => x.ChargePointId == device.Id && x.ConnectorNumber == number,
                ct
            )
            ?? throw new OcppFault("PropertyConstraintViolation", "Connector is not provisioned.");
        var active = await db.OcppChargingTransactions.SingleOrDefaultAsync(
            x =>
                x.ChargePointId == device.Id
                && x.ConnectorNumber == number
                && x.PhysicalClosedAt == null,
            ct
        );
        if (active is not null)
        {
            if (active.StoppedAt is null)
                throw new OcppFault(
                    "OccurrenceConstraintViolation",
                    "The connector already has an active transaction."
                );
            active.PhysicalClosedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }
        var station = await db.Stations.SingleOrDefaultAsync(x => x.Id == connector.StationId, ct);
        var email = configuration["Demo:DriverEmail"]?.Trim().ToUpperInvariant();
        var driverId = email is null
            ? null
            : await db
                .Accounts.Where(x =>
                    x.NormalizedEmail == email
                    && x.Role == UserRole.Driver
                    && x.IsActive
                    && x.ArchivedAt == null
                )
                .Select(x => (Guid?)x.Id)
                .SingleOrDefaultAsync(ct);
        var transaction = new OcppChargingTransaction
        {
            ChargePointId = device.Id,
            ConnectorNumber = number,
            StationId = connector.StationId,
            DriverId = driverId,
            StartMessageId = message.Id,
            StartedAt = started,
            StartReceivedAt = clock.GetUtcNow(),
            MeterStartWh = meter,
            EnergyKWh = 0,
            UnitPriceSnapshot = station?.PricePerKWh,
            Currency = station?.Currency,
        };
        db.OcppChargingTransactions.Add(transaction);
        await projection.SetConnectorAsync(device.Id, number, "Preparing", started, ct);
        await db.SaveChangesAsync(ct);
        StageSessionChange(db, transaction, clock.GetUtcNow());
        return new
        {
            transactionId = transaction.Ocpp16TransactionId,
            idTagInfo = new { status = "Accepted" },
        };
    }

    public async Task<object> StopAsync(
        OcppChargePoint device,
        OcppInboundMessage message,
        JsonElement payload,
        CancellationToken ct
    )
    {
        var transactionId = OcppPayload.Integer(payload, "transactionId", 1);
        var meterStop = OcppPayload.Integer(payload, "meterStop");
        var stopped = OcppPayload.Timestamp(payload);
        var reason = OcppPayload.OptionalText(payload, "reason", 40);
        var samples = OcppPayload.Samples(payload, "transactionData");
        var transaction =
            await db.OcppChargingTransactions.SingleOrDefaultAsync(
                x => x.ChargePointId == device.Id && x.Ocpp16TransactionId == transactionId,
                ct
            ) ?? throw new OcppFault("PropertyConstraintViolation", "Unknown transaction.");
        if (stopped < transaction.StartedAt)
            throw new OcppFault(
                "PropertyConstraintViolation",
                "Stop time precedes transaction start."
            );
        if (transaction.StopMessageId is not null)
            return new { };
        meters.AddSamples(device.Id, message.Id, transaction.ConnectorNumber, transaction, samples);
        transaction.StopMessageId = message.Id;
        transaction.StoppedAt = stopped;
        transaction.StopReceivedAt = clock.GetUtcNow();
        transaction.State = "Stopped";
        transaction.StopReason = reason;
        transaction.MeterStopWh = meterStop;
        transaction.CurrentPowerKW = 0;
        transaction.EnergyKWh =
            meterStop >= transaction.MeterStartWh
                ? (meterStop - transaction.MeterStartWh) / 1000m
                : null;
        if (transaction.EnergyKWh is { } energy && transaction.UnitPriceSnapshot is { } price)
            transaction.CalculatedCost = decimal.Round(
                energy * price,
                2,
                MidpointRounding.AwayFromZero
            );
        if (
            transaction.DriverId is { } driver
            && transaction.StationId is { } station
            && transaction.EnergyKWh is { } total
            && transaction.UnitPriceSnapshot is { } tariff
            && transaction.CalculatedCost is { } cost
        )
            db.ChargingHistory.Add(
                new ChargingHistory
                {
                    DriverId = driver,
                    StationId = station,
                    StartedAt = transaction.StartedAt,
                    EndedAt = stopped,
                    EnergyKWh = total,
                    UnitPriceSnapshot = tariff,
                    TotalCost = cost,
                    Currency = transaction.Currency ?? "EUR",
                    IsDemo = true,
                }
            );
        await projection.SetConnectorAsync(
            device.Id,
            transaction.ConnectorNumber,
            "Finishing",
            stopped,
            ct
        );
        StageSessionChange(db, transaction, clock.GetUtcNow());
        return new { };
    }

    internal static void StageSessionChange(
        ChargingDbContext db,
        OcppChargingTransaction transaction,
        DateTimeOffset now
    )
    {
        if (transaction.DriverId is not { } driver || transaction.StationId is not { } station)
            return;
        db.RealtimeOutbox.Add(
            new RealtimeOutboxEvent
            {
                EventType = "SessionChanged",
                EntityType = "OcppTransaction",
                EntityId = transaction.Id,
                Audience = "Driver",
                RecipientAccountId = driver,
                PayloadJson = JsonSerializer.Serialize(
                    new StationChanged(station, 0, transaction.State)
                ),
                CreatedAt = now,
                AvailableAt = now,
            }
        );
    }
}
