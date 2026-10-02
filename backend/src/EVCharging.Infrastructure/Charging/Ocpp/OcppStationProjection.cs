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
        CancellationToken ct
    )
    {
        var now = clock.GetUtcNow();
        var connectors = await db
            .OcppConnectors.Where(x =>
                x.ChargePointId == deviceId && (number == 0 || x.ConnectorNumber == number)
            )
            .ToListAsync(ct);
        foreach (var connector in connectors)
        {
            if (number == 0 && status is not ("Unavailable" or "Faulted"))
                continue;
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
            if (connector.StatusObservedAt is { } previous && observed < previous)
                continue;
            connector.ReportedStatus = status;
            connector.StatusObservedAt = observed;
            connector.StatusReceivedAt = now;
            connector.Version++;
            if (connector.BusinessConnectorId is { } id)
                await receiver.ReceiveAsync(
                    new(
                        "OCPP",
                        Guid.NewGuid().ToString(),
                        deviceId.ToString(),
                        number.ToString(),
                        null,
                        ChargerEventKind.StatusReported,
                        observed,
                        null,
                        null,
                        null,
                        ConnectorId: id,
                        ReceivedAt: now,
                        Status: Map(status)
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
