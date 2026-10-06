using System.Text.Json;
using EVCharging.Application.Charging;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EVCharging.Infrastructure.Charging.Ocpp;

public sealed class OcppTransactionHandler(
    ChargingDbContext db,
    OcppStationProjection projection,
    OcppMeterHandler meters,
    TimeProvider clock,
    IOptions<OcppOptions> options,
    IChargerEventReceiver receiver,
    OcppHardwareDiscovery discovery
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
        var connector = await discovery.ConnectorAsync(device.Id, number, ct);
        if (
            connector.BusinessConnectorId is not null
            && !DeviceTagAuthorization.Accepted(device, idTag, options.Value)
        )
            throw new OcppFault(
                "SecurityError",
                "The authorization tag is not registered for this charge point."
            );
        if (
            connector.BusinessConnectorId is not null
            && !await db.Stations.AnyAsync(x => x.Id == connector.StationId && !x.IsArchived, ct)
        )
            throw new OcppFault(
                "PropertyConstraintViolation",
                "The station is archived or unavailable."
            );
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
                    $"The connector already has active transaction {active.Ocpp16TransactionId}. Send StopTransaction for that transaction before starting another."
                );
            active.PhysicalClosedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }
        var now = clock.GetUtcNow();
        var lastStop = await db
            .OcppChargingTransactions.Where(x =>
                x.ChargePointId == device.Id && x.ConnectorNumber == number && x.StoppedAt != null
            )
            .MaxAsync(x => x.StopReceivedAt, ct);
        var startStatus = await db
            .OcppStatusEvents.Where(x =>
                x.ChargePointId == device.Id
                && x.ConnectorNumber == number
                && x.ReceivedAt >= now.AddSeconds(-10)
                && (lastStop == null || x.ReceivedAt > lastStop)
                && x.ObservedAt >= started
                && x.ObservedAt <= started.AddSeconds(5)
            )
            .OrderByDescending(x => x.ReceivedAt)
            .FirstOrDefaultAsync(ct);
        if (
            options.Value.SimulatorEnabled
            && connector.StatusObservedAt > started
            && await db.Stations.AnyAsync(x => x.Id == connector.StationId && x.IsDemo, ct)
        )
            connector.StatusObservedAt = null;
        await projection.SetConnectorAsync(
            device.Id,
            number,
            startStatus?.Status ?? "Preparing",
            startStatus?.ObservedAt ?? started,
            ct
        );
        var transaction = new OcppChargingTransaction
        {
            ChargePointId = device.Id,
            ConnectorNumber = number,
            StationId = connector.StationId,
            StartMessageId = message.Id,
            StartedAt = started,
            StartReceivedAt = now,
            MeterStartWh = meter,
        };
        if (connector.BusinessConnectorId is null)
        {
            db.OcppChargingTransactions.Add(transaction);
            await db.SaveChangesAsync(ct);
            return new
            {
                transactionId = transaction.Ocpp16TransactionId,
                idTagInfo = new { status = "Accepted" },
            };
        }
        // Stable normalized session identity; protocol records link to it explicitly.
        transaction.SessionId = transaction.Id;
        await receiver.ReceiveAsync(
            new(
                "OCPP",
                message.Id.ToString(),
                device.Id.ToString(),
                number.ToString(),
                null,
                ChargerEventKind.Started,
                started,
                0,
                null,
                null,
                transaction.SessionId,
                connector.BusinessConnectorId,
                now,
                AssignedDriverId: device.ProvisionedSerialNumber is not null
                && (device.DriverCardIdTag is null || device.DriverCardIdTag == idTag)
                    ? device.AssignedDriverId
                    : null
            ),
            ct
        );
        db.OcppChargingTransactions.Add(transaction);
        await db.SaveChangesAsync(ct);
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
        if (reason is not null && !OcppValues.StopReasons.Contains(reason))
            throw new OcppFault("PropertyConstraintViolation", "Unknown stop reason.");
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
        var simulation =
            options.Value.SimulatorEnabled
            && await db.Stations.AnyAsync(x => x.Id == transaction.StationId && x.IsDemo, ct);
        await meters.AddSamplesAsync(
            device.Id,
            message.Id,
            transaction.ConnectorNumber,
            transaction,
            samples,
            simulation,
            ct
        );
        transaction.StopMessageId = message.Id;
        transaction.StoppedAt = stopped;
        transaction.StopReceivedAt = clock.GetUtcNow();
        transaction.State = "Stopped";
        transaction.StopReason = reason;
        transaction.MeterStopWh = meterStop;
        if (transaction.SessionId is null)
        {
            await projection.SetConnectorAsync(
                device.Id,
                transaction.ConnectorNumber,
                "Finishing",
                stopped,
                ct
            );
            return new { };
        }
        var connector = await db.OcppConnectors.SingleAsync(
            x => x.ChargePointId == device.Id && x.ConnectorNumber == transaction.ConnectorNumber,
            ct
        );
        decimal? finalEnergy =
            meterStop >= transaction.MeterStartWh
                ? (meterStop - transaction.MeterStartWh) / 1000m
                : null;
        var session = await db.ChargingSessions.SingleAsync(x => x.Id == transaction.SessionId, ct);
        var meterRegression = finalEnergy is null || finalEnergy < session.EnergyKWh;
        if (meterRegression)
            finalEnergy = null;
        await receiver.ReceiveAsync(
            new(
                "OCPP",
                message.Id.ToString(),
                device.Id.ToString(),
                transaction.ConnectorNumber.ToString(),
                transactionId.ToString(),
                ChargerEventKind.Stopped,
                stopped,
                finalEnergy,
                null,
                null,
                transaction.SessionId,
                connector.BusinessConnectorId,
                clock.GetUtcNow(),
                StopReason: reason,
                CompletionWarning: meterRegression ? "FinalMeterRegression" : null
            ),
            ct
        );
        await projection.SetConnectorAsync(
            device.Id,
            transaction.ConnectorNumber,
            "Finishing",
            stopped,
            ct
        );
        return new { };
    }
}
