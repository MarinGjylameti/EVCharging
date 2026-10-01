using System.Text.Json;
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
    TimeProvider clock
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
                return new
                {
                    idTagInfo = new
                    {
                        status = OcppPayload.Text(payload, "idTag", 20)
                        == options.Value.SimulatorTagId
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
            default:
                throw new OcppFault("NotSupported", "This action is not supported.");
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
        var observed = payload.TryGetProperty("timestamp", out _)
            ? OcppPayload.Timestamp(payload)
            : clock.GetUtcNow();
        var info = OcppPayload.OptionalText(payload, "info", 80);
        var vendorId = OcppPayload.OptionalText(payload, "vendorId", 255);
        var vendorError = OcppPayload.OptionalText(payload, "vendorErrorCode", 50);
        if (
            number > 0
            && !await db.OcppConnectors.AnyAsync(
                x => x.ChargePointId == device.Id && x.ConnectorNumber == number,
                ct
            )
        )
            throw new OcppFault("PropertyConstraintViolation", "Connector is not provisioned.");
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
        await projection.SetConnectorAsync(device.Id, number, status, observed, ct);
        return new { };
    }
}
