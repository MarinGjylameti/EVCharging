using System.Text.Json;
using EVCharging.Application.Charging;
using EVCharging.Domain.Charging;
using EVCharging.Infrastructure.Persistence;

namespace EVCharging.Infrastructure.Charging;

internal static class ChargingRealtime
{
    public static void Stage(
        ChargingDbContext db,
        Guid stationId,
        string eventType,
        string entityType,
        Guid entityId,
        long version,
        DateTimeOffset now,
        Guid? driverId = null,
        DriverStationDto? station = null,
        DriverChargingSession? session = null,
        DriverConnectorState? connector = null,
        ChargingCommandResult? command = null
    )
    {
        var entry = new RealtimeOutboxEvent
        {
            EventType = eventType,
            EntityType = entityType,
            EntityId = entityId,
            EntityVersion = version,
            Audience = driverId is null ? "PublicStation" : "Driver",
            RecipientAccountId = driverId,
            CreatedAt = now,
            AvailableAt = now,
        };
        entry.PayloadJson = JsonSerializer.Serialize(
            new StationChanged(
                stationId,
                station?.Version ?? 0,
                station?.Status ?? "Changed",
                driverId,
                entry.Id,
                eventType,
                entityType,
                entityId,
                version,
                now,
                station,
                session,
                connector,
                command
            )
        );
        db.RealtimeOutbox.Add(entry);
    }

    public static DriverConnectorState ConnectorDto(ChargingConnector connector) =>
        new(
            connector.IsOnline ? connector.Status.ToString() : "ConnectionLost",
            connector.FaultCode,
            connector.FaultInfo,
            connector.Version,
            connector.DeviceStatus,
            connector.DeviceFaultCode,
            connector.DeviceFaultInfo
        );

    public static DriverStationDto StationDto(ChargingStation station, DateTimeOffset now) =>
        new(
            station.Id,
            station.Code,
            station.Name,
            station.City,
            station.Address,
            station.Latitude,
            station.Longitude,
            station.ConnectorType,
            station.ConnectorCount,
            station.PowerKW,
            station.PricePerKWh,
            station.Currency,
            station.Status.ToString(),
            station.IsDemo,
            station.Version,
            AvailabilityForecastRules.ForDriver(station, now),
            station.StatusSource,
            station.StatusObservedAt,
            station.StatusReceivedAt,
            station.WarningCode
        );

    public static DriverChargingSession SessionDto(
        ChargingSession session,
        ChargingConnector connector,
        ChargingStation station,
        DateTimeOffset now
    )
    {
        var ended = session.EndedAt is not null;
        var status =
            ended ? ChargingState.Completed.ToString()
            : !connector.IsOnline ? "ConnectionLost"
            : session.Status.ToString();
        var powerUntil = session.PowerReceivedAt?.AddSeconds(120);
        var power =
            ended ? 0
            : status == nameof(ChargingState.Charging) && powerUntil > now ? session.PowerKW
            : null;
        var skewed =
            (session.StartReceivedAt - session.StartedAt).Duration() > TimeSpan.FromMinutes(5)
            || session.LastMeasurementReceivedAt is { } received
                && (received - session.LastMeasurementAt).Duration() > TimeSpan.FromMinutes(5);
        return new(
            session.Id,
            station.Id,
            station.Name,
            station.Code,
            session.ConnectorNumber,
            status,
            session.EnergyKWh,
            power,
            session.StateOfCharge,
            session.TemperatureCelsius,
            session.StartedAt,
            session.EndedAt,
            session.LastMeasurementReceivedAt is null ? null : session.LastMeasurementAt,
            session.CalculatedCost,
            session.Currency,
            session.IsDemo,
            session.StartReceivedAt,
            session.StopReceivedAt,
            session.LastMeasurementReceivedAt,
            skewed,
            session.EnergyScope,
            session.PowerScope,
            session.UnitPriceSnapshot,
            power is not null && !ended ? "Reported" : null,
            power is not null && !ended ? powerUntil : null,
            session.Version,
            ended ? null : connector.FaultCode,
            ended ? null : connector.FaultInfo,
            session.LatestReadingsJson is null
                ? null
                : JsonSerializer.Deserialize<ChargingSampleDiagnostic[]>(
                    session.LatestReadingsJson
                ),
            session.StopReason,
            session.CompletionWarning,
            session.BatteryCapacityKWh,
            session.SocReceivedAt?.AddMinutes(5)
        );
    }
}
