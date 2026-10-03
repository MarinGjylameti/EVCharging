using EVCharging.Application.Charging;
using EVCharging.Domain.Charging;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EVCharging.Infrastructure.Charging.Ocpp;

public sealed class OcppStationProjection(
    ChargingDbContext db,
    TimeProvider clock,
    IOptions<OcppOptions> options,
    IChargerEventReceiver receiver
)
{
    public async Task SetConnectorAsync(
        Guid deviceId,
        int number,
        string status,
        DateTimeOffset observed,
        CancellationToken ct,
        string? errorCode = null,
        string? info = null
    )
    {
        var now = clock.GetUtcNow();
        var device = await db.OcppChargePoints.SingleAsync(x => x.Id == deviceId, ct);
        var connectors = await db
            .OcppConnectors.Where(x =>
                x.ChargePointId == deviceId && (number == 0 || x.ConnectorNumber == number)
            )
            .ToListAsync(ct);
        foreach (var connector in connectors)
        {
            var simulation =
                options.Value.SimulatorEnabled
                && await db.Stations.AnyAsync(
                    x => x.Id == connector.StationId && x.IsDemo && !x.IsArchived,
                    ct
                );
            if (
                simulation
                    ? observed > now.AddDays(1)
                    : observed < now.AddMinutes(-5) || observed > now.AddMinutes(5)
            )
                continue;
            var previous = number == 0 ? device.StatusObservedAt : connector.StatusObservedAt;
            // Demo forms retain a fixed timestamp while accelerated runs advance it.
            // Live demo messages are serialized by the gateway; use their receipt order.
            // Physical chargers retain event-time ordering and the freshness window.
            if (!simulation && previous is not null && observed < previous)
                continue;
            if (number == 0)
            {
                device.ReportedStatus = status;
                device.ErrorCode = errorCode;
                device.ErrorInfo = info;
                device.StatusObservedAt = observed;
            }
            else
            {
                connector.ReportedStatus = status;
                connector.StatusObservedAt = observed;
                connector.StatusReceivedAt = now;
                // Synthetic start/stop projections must not clear an actual device fault.
                if (errorCode is not null)
                {
                    connector.ErrorCode = errorCode;
                    connector.ErrorInfo = info;
                }
            }
            var fault = connector.ErrorCode is not (null or "NoError") ? connector.ErrorCode : null;
            // OCPP 1.6 permits warning codes while charging continues. Only the
            // reported Faulted status declares an unavailable faulted connector.
            var effectiveStatus = Map(connector.ReportedStatus);
            connector.Version++;
            if (connector.BusinessConnectorId is { } id)
                await receiver.ReceiveAsync(
                    new(
                        "OCPP",
                        Guid.NewGuid().ToString(),
                        deviceId.ToString(),
                        connector.ConnectorNumber.ToString(),
                        null,
                        ChargerEventKind.StatusReported,
                        observed,
                        null,
                        null,
                        null,
                        ConnectorId: id,
                        ReceivedAt: now,
                        Status: effectiveStatus,
                        FaultCode: fault,
                        FaultInfo: fault is null ? null : connector.ErrorInfo,
                        DeviceStatus: device.ReportedStatus,
                        DeviceFaultCode: device.ErrorCode is not (null or "NoError")
                            ? device.ErrorCode
                            : null,
                        DeviceFaultInfo: device.ErrorCode is not (null or "NoError")
                            ? device.ErrorInfo
                            : null
                    ),
                    ct
                );
        }
    }

    public async Task InvalidateDeviceAsync(Guid deviceId, CancellationToken ct)
    {
        var connectors = await db
            .OcppConnectors.Where(x => x.ChargePointId == deviceId)
            .ToListAsync(ct);
        foreach (var connector in connectors)
        {
            connector.ReportedStatus = null;
            connector.StatusObservedAt = null;
            connector.StatusReceivedAt = clock.GetUtcNow();
            connector.Version++;
            if (connector.BusinessConnectorId is { } id)
                await receiver.ReceiveAsync(
                    new(
                        "OCPP",
                        Guid.NewGuid().ToString(),
                        deviceId.ToString(),
                        connector.ConnectorNumber.ToString(),
                        null,
                        ChargerEventKind.Disconnected,
                        clock.GetUtcNow(),
                        null,
                        null,
                        null,
                        ConnectorId: id,
                        ReceivedAt: clock.GetUtcNow()
                    ),
                    ct
                );
        }
    }

    public async Task ConnectedAsync(Guid deviceId, CancellationToken ct)
    {
        var connectors = await db
            .OcppConnectors.Where(x => x.ChargePointId == deviceId)
            .ToListAsync(ct);
        foreach (var connector in connectors)
            if (connector.BusinessConnectorId is { } id)
                await receiver.ReceiveAsync(
                    new(
                        "OCPP",
                        Guid.NewGuid().ToString(),
                        deviceId.ToString(),
                        connector.ConnectorNumber.ToString(),
                        null,
                        ChargerEventKind.Connected,
                        clock.GetUtcNow(),
                        null,
                        null,
                        null,
                        ConnectorId: id,
                        ReceivedAt: clock.GetUtcNow(),
                        Status: ChargingState.Unknown
                    ),
                    ct
                );
    }

    public static ChargingState Map(string? status) =>
        status switch
        {
            "Available" => ChargingState.Available,
            "Preparing" => ChargingState.Connecting,
            "Charging" => ChargingState.Charging,
            "SuspendedEV" => ChargingState.PausedByVehicle,
            "SuspendedEVSE" => ChargingState.PausedByCharger,
            "Finishing" => ChargingState.Finishing,
            "Reserved" => ChargingState.Reserved,
            "Faulted" => ChargingState.Faulted,
            "Unavailable" => ChargingState.Unavailable,
            _ => ChargingState.Unknown,
        };
}
