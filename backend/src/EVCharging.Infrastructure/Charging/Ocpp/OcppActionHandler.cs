using System.Text.Json;
using EVCharging.Domain.Charging;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EVCharging.Infrastructure.Charging.Ocpp;

public sealed class OcppActionHandler(
    ChargingDbContext db,
    OcppTransactionHandler transactions,
    OcppMeterHandler meters,
    OcppStationProjection projection,
    IOptions<OcppOptions> options,
    TimeProvider clock,
    StationProjection stations,
    OcppHardwareDiscovery discovery
)
{
    public async Task<object> HandleAsync(
        OcppChargePoint device,
        OcppInboundMessage message,
        JsonElement payload,
        CancellationToken ct
    )
    {
        switch (message.Action)
        {
            case "BootNotification":
                return Boot(device, payload);
            case "Heartbeat":
                device.LastHeartbeatAt = clock.GetUtcNow();
                return new { currentTime = clock.GetUtcNow() };
            case "Authorize":
                var tag = OcppPayload.Text(payload, "idTag", 20);
                return new
                {
                    idTagInfo = new
                    {
                        status = (
                            device.ProvisionedSerialNumber is not null
                            && !await db.OcppConnectors.AnyAsync(
                                x => x.ChargePointId == device.Id && x.BusinessConnectorId != null,
                                ct
                            )
                        ) || DeviceTagAuthorization.Accepted(device, tag, options.Value)
                            ? "Accepted"
                            : "Invalid",
                    },
                };
            case "StatusNotification":
                return await StatusAsync(device, message, payload, ct);
            case "StartTransaction":
                return await transactions.StartAsync(device, message, payload, ct);
            case "StopTransaction":
                return await transactions.StopAsync(device, message, payload, ct);
            case "MeterValues":
                await meters.RecordAsync(device.Id, message, payload, ct);
                return new { };
            case "DataTransfer":
                return await EstimateAsync(device, payload, ct);
            default:
                throw new OcppFault("NotSupported", "This action is not supported.");
        }
    }

    // This vendor extension is restricted to enabled, allowlisted demo chargers.
    // It carries only a transaction-bound duration; no account or battery profile.
    private async Task<object> EstimateAsync(
        OcppChargePoint device,
        JsonElement payload,
        CancellationToken ct
    )
    {
        if (
            OcppPayload.Text(payload, "vendorId", 255) != "EVCharging.Simulator"
            || !options.Value.SimulatorEnabled
            || !options.Value.SimulatorIdentities.Contains(device.Identity)
        )
            return new { status = "UnknownVendorId" };
        if (OcppPayload.OptionalText(payload, "messageId", 50) != "ChargingEstimate")
            return new { status = "UnknownMessageId" };
        JsonDocument data;
        try
        {
            data = JsonDocument.Parse(OcppPayload.Text(payload, "data", 1024));
        }
        catch (JsonException)
        {
            throw new OcppFault("FormationViolation", "Invalid estimate data.");
        }
        using (data)
        {
            if (data.RootElement.ValueKind != JsonValueKind.Object)
                throw new OcppFault("FormationViolation", "Estimate data must be an object.");
            var transactionId = OcppPayload.Integer(data.RootElement, "transactionId", 1);
            var seconds = OcppPayload.Integer(data.RootElement, "remainingSeconds");
            if (seconds > 86400)
                throw new OcppFault("PropertyConstraintViolation", "Estimate exceeds 24 hours.");
            var transaction = await db.OcppChargingTransactions.SingleOrDefaultAsync(
                x =>
                    x.ChargePointId == device.Id
                    && x.Ocpp16TransactionId == transactionId
                    && x.StoppedAt == null,
                ct
            );
            if (transaction is null)
                return new { status = "Rejected" };
            var session = await db.ChargingSessions.SingleAsync(
                x => x.Id == transaction.SessionId,
                ct
            );
            var station = await db.Stations.SingleAsync(x => x.Id == session.PointId, ct);
            if (!station.IsDemo)
                return new { status = "Rejected" };
            var now = clock.GetUtcNow();
            await stations.RefreshAsync(
                station.Id,
                ct,
                new AvailabilityForecast(
                    session.ConnectorNumber,
                    now.AddSeconds(seconds),
                    now,
                    "Simulator",
                    now.AddSeconds(120)
                )
            );
            return new { status = "Accepted" };
        }
    }

    private object Boot(OcppChargePoint device, JsonElement payload)
    {
        var vendor = OcppPayload.Text(payload, "chargePointVendor", 20);
        var model = OcppPayload.Text(payload, "chargePointModel", 20);
        var pointSerial = OcppPayload.OptionalText(payload, "chargePointSerialNumber", 25);
        var boxSerial = OcppPayload.OptionalText(payload, "chargeBoxSerialNumber", 25);
        var firmware = OcppPayload.OptionalText(payload, "firmwareVersion", 50);
        var iccid = OcppPayload.OptionalText(payload, "iccid", 20);
        var imsi = OcppPayload.OptionalText(payload, "imsi", 20);
        var meterType = OcppPayload.OptionalText(payload, "meterType", 25);
        var meterSerial = OcppPayload.OptionalText(payload, "meterSerialNumber", 25);
        device.Vendor = vendor;
        device.Model = model;
        device.ChargePointSerialNumber = pointSerial;
        device.ChargeBoxSerialNumber = boxSerial;
        device.FirmwareVersion = firmware;
        device.Iccid = iccid;
        device.Imsi = imsi;
        device.MeterType = meterType;
        device.MeterSerialNumber = meterSerial;
        device.LastBootAt = clock.GetUtcNow();
        return new
        {
            status = "Accepted",
            currentTime = clock.GetUtcNow(),
            interval = options.Value.HeartbeatSeconds,
        };
    }

    private async Task<object> StatusAsync(
        OcppChargePoint device,
        OcppInboundMessage message,
        JsonElement payload,
        CancellationToken ct
    )
    {
        var number = OcppPayload.Integer(payload, "connectorId");
        var status = OcppPayload.Text(payload, "status", 30);
        if (
            status
            is not (
                "Available"
                or "Preparing"
                or "Charging"
                or "SuspendedEVSE"
                or "SuspendedEV"
                or "Finishing"
                or "Reserved"
                or "Unavailable"
                or "Faulted"
            )
        )
            throw new OcppFault("PropertyConstraintViolation", "Unknown connector status.");
        var error = OcppPayload.Text(payload, "errorCode", 50);
        if (!OcppValues.ErrorCodes.Contains(error))
            throw new OcppFault("PropertyConstraintViolation", "Unknown error code.");
        if (number == 0 && status is not ("Available" or "Unavailable" or "Faulted"))
            throw new OcppFault(
                "PropertyConstraintViolation",
                "Connector 0 reports station availability or faults."
            );
        var observed = payload.TryGetProperty("timestamp", out _)
            ? OcppPayload.Timestamp(payload)
            : clock.GetUtcNow();
        var info = OcppPayload.OptionalText(payload, "info", 50);
        var vendorId = OcppPayload.OptionalText(payload, "vendorId", 255);
        var vendorError = OcppPayload.OptionalText(payload, "vendorErrorCode", 50);
        if (number > 0)
            await discovery.ConnectorAsync(device.Id, number, ct);
        db.OcppStatusEvents.Add(
            new OcppStatusEvent
            {
                ChargePointId = device.Id,
                MessageId = message.Id,
                ConnectorNumber = number,
                Status = status,
                ErrorCode = error,
                ObservedAt = observed,
                ReceivedAt = clock.GetUtcNow(),
                Info = info,
                VendorId = vendorId,
                VendorErrorCode = vendorError,
            }
        );
        if (
            status == "Available"
            && observed >= clock.GetUtcNow().AddMinutes(-5)
            && observed <= clock.GetUtcNow().AddMinutes(5)
        )
        {
            var stopped = await db
                .OcppChargingTransactions.Where(x =>
                    x.ChargePointId == device.Id
                    && x.ConnectorNumber == number
                    && x.PhysicalClosedAt == null
                    && x.StoppedAt != null
                    && x.StoppedAt <= observed
                )
                .ToListAsync(ct);
            foreach (var transaction in stopped)
                transaction.PhysicalClosedAt = clock.GetUtcNow();
        }
        await projection.SetConnectorAsync(device.Id, number, status, observed, ct, error, info);
        return new { };
    }
}
