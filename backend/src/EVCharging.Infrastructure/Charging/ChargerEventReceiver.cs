using System.Text.Json;
using EVCharging.Application.Charging;
using EVCharging.Domain.Charging;
using EVCharging.Domain.Identity;
using EVCharging.Domain.Notifications;
using EVCharging.Infrastructure.Notifications;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EVCharging.Infrastructure.Charging;

// Participates in the adapter's transaction; never commits independently.
// Protocol inbox deduplication happens before normalized events reach this port.
public sealed class ChargerEventReceiver(
    ChargingDbContext db,
    StationProjection projection,
    TimeProvider clock,
    NotificationService notifications,
    OperationsNotifications operators
) : IChargerEventReceiver
{
    public async Task ReceiveAsync(ChargerEvent change, CancellationToken ct)
    {
        var connector = await db.ChargingConnectors.SingleAsync(
            x => x.Id == change.ConnectorId,
            ct
        );
        var now = change.ReceivedAt ?? clock.GetUtcNow();
        if (
            change.Kind
            is ChargerEventKind.Connected
                or ChargerEventKind.Disconnected
                or ChargerEventKind.StatusReported
                or ChargerEventKind.Faulted
        )
        {
            var priorStatus = connector.Status;
            var priorOnline = connector.IsOnline;
            if (change.Kind == ChargerEventKind.StatusReported)
            {
                connector.FaultCode = change.FaultCode;
                connector.FaultInfo = change.FaultInfo;
                connector.DeviceStatus = change.DeviceStatus;
                connector.DeviceFaultCode = change.DeviceFaultCode;
                connector.DeviceFaultInfo = change.DeviceFaultInfo;
            }
            connector.IsOnline = change.Kind != ChargerEventKind.Disconnected;
            if (change.Status is { } status)
                connector.Status = status;
            if (change.Kind == ChargerEventKind.Faulted)
                connector.Status = ChargingState.Faulted;
            connector.StatusObservedAt = change.ObservedAt;
            connector.StatusReceivedAt = now;
            connector.Version++;
            if (priorStatus != connector.Status || priorOnline != connector.IsOnline)
            {
                DeviceAudit(
                    connector.StationId,
                    change.Kind == ChargerEventKind.Disconnected ? "ChargerDisconnected"
                        : !priorOnline ? "ChargerConnected"
                        : "ChargerStatusChanged",
                    new
                    {
                        Connector = connector.Number,
                        Before = priorStatus.ToString(),
                        After = connector.Status.ToString(),
                        connector.IsOnline,
                        connector.FaultCode,
                    },
                    now
                );
                if (change.Kind == ChargerEventKind.Disconnected && priorOnline)
                    await operators.EnqueueAsync(
                        connector.StationId,
                        $"connection-lost:{connector.Id}:{connector.Version}",
                        NotificationKind.UnexpectedDisconnect,
                        ct
                    );
                if (
                    connector.Status == ChargingState.Faulted
                    && priorStatus != ChargingState.Faulted
                )
                    await operators.EnqueueAsync(
                        connector.StationId,
                        $"charger-fault:{connector.Id}:{connector.Version}",
                        NotificationKind.ChargerFaulted,
                        ct
                    );
                if (
                    connector.Status == ChargingState.Unavailable
                    && priorStatus != ChargingState.Unavailable
                )
                    await operators.EnqueueAsync(
                        connector.StationId,
                        $"charger-unavailable:{connector.Id}:{connector.Version}",
                        NotificationKind.PointOutOfService,
                        ct
                    );
            }
            var active = await db.ChargingSessions.SingleOrDefaultAsync(
                x => x.ConnectorId == connector.Id && x.EndedAt == null,
                ct
            );
            if (active is { EndedAt: null })
            {
                if (change.Status is not null || change.Kind == ChargerEventKind.Faulted)
                    active.Status = connector.Status;
                if (priorStatus != connector.Status || priorOnline != connector.IsOnline)
                {
                    active.PowerKW = null;
                    active.PowerReceivedAt = null;
                }
                active.Version++;
                await NotifySessionAsync(active, "SessionStatusChanged", now, ct);
                if (change.Kind == ChargerEventKind.Disconnected && priorOnline)
                {
                    var owner = await db.ChargingConnectionRequests.SingleOrDefaultAsync(
                        x => x.SessionId == active.Id && x.State == "Linked",
                        ct
                    );
                    if (owner is not null)
                        await notifications.EnqueueAsync(
                            owner.DriverId,
                            $"connection-lost:{connector.Id}:{connector.Version}",
                            NotificationKind.UnexpectedDisconnect,
                            ct
                        );
                }
            }
            ChargingRealtime.Stage(
                db,
                connector.StationId,
                "ConnectorChanged",
                "ChargingConnector",
                connector.Id,
                connector.Version,
                now,
                connector: ChargingRealtime.ConnectorDto(connector)
            );
            await projection.RefreshAsync(connector.StationId, ct);
            return;
        }

        if (change.SessionId is not { } sessionId)
            throw new InvalidOperationException("A physical session identity is required.");
        var session =
            db.ChargingSessions.Local.SingleOrDefault(x => x.Id == sessionId)
            ?? await db.ChargingSessions.SingleOrDefaultAsync(x => x.Id == sessionId, ct);
        if (change.Kind == ChargerEventKind.Started)
        {
            if (session is not null)
                return;
            var station = await db.Stations.SingleAsync(x => x.Id == connector.StationId, ct);
            session = new ChargingSession(
                station.Id,
                station.PricePerKWh,
                station.Currency,
                change.ObservedAt
            )
            {
                Id = sessionId,
                ConnectorId = connector.Id,
                ConnectorNumber = connector.Number,
                StartReceivedAt = now,
                IsDemo = station.IsDemo,
                Status = connector.Status,
                Version = 1,
            };
            db.ChargingSessions.Add(session);
            ChargingRealtime.StageOperations(
                db,
                station.Id,
                "SessionStarted",
                "ChargingSession",
                session.Id,
                session.Version,
                now
            );
            DeviceAudit(
                station.Id,
                "ChargingStarted",
                new
                {
                    session.Id,
                    Connector = connector.Number,
                    session.StartedAt,
                },
                now
            );
            var waiting = await db.ChargingConnectionRequests.SingleOrDefaultAsync(
                x =>
                    x.StationId == station.Id
                    && x.ConnectorNumber == connector.Number
                    && x.State == "Waiting",
                ct
            );
            if (waiting is null && change.AssignedDriverId is { } assignedDriver)
            {
                waiting = new ChargingConnectionRequest
                {
                    DriverId = assignedDriver,
                    StationId = station.Id,
                    ConnectorNumber = connector.Number,
                    IsAutomatic = true,
                    CreatedAt = now,
                    ExpiresAt = now.AddMinutes(30),
                };
                db.ChargingConnectionRequests.Add(waiting);
            }
            if (waiting is not null)
            {
                // Match the claim/profile lock order: device, then account. A profile
                // update cannot race the capacity snapshot taken when charging starts.
                if (db.Database.IsRelational())
                    await db.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT 1 FROM accounts WHERE id = {waiting.DriverId} FOR UPDATE",
                        ct
                    );
                var driver = await db.Accounts.SingleOrDefaultAsync(
                    x => x.Id == waiting.DriverId,
                    ct
                );
                if (waiting.ExpiresAt <= now)
                    waiting.State = "Expired";
                else if (
                    driver is null
                    || !driver.IsActive
                    || driver.ArchivedAt is not null
                    || driver.Role != UserRole.Driver
                )
                    waiting.State = "Cancelled";
                else
                {
                    waiting.State = "Linked";
                    waiting.SessionId = session.Id;
                    waiting.LinkedAt = now;
                    session.BatteryCapacityKWh = driver.BatteryCapacityKWh;
                    await notifications.EnqueueAsync(
                        driver.Id,
                        $"charging-started:{session.Id}",
                        NotificationKind.ChargingStarted,
                        ct
                    );
                    ChargingRealtime.Stage(
                        db,
                        station.Id,
                        "SessionStarted",
                        "ChargingSession",
                        session.Id,
                        session.Version,
                        now,
                        waiting.DriverId,
                        session: ChargingRealtime.SessionDto(session, connector, station, now)
                    );
                }
            }
            // Also notify open, currently unclaimed charger views without private data.
            await operators.EnqueueAsync(
                station.Id,
                $"charging-started:{session.Id}",
                NotificationKind.ChargingStarted,
                ct
            );
            await projection.RefreshAsync(station.Id, ct);
            ChargingRealtime.Stage(
                db,
                station.Id,
                "ConnectorChanged",
                "ChargingConnector",
                connector.Id,
                connector.Version,
                now,
                connector: ChargingRealtime.ConnectorDto(connector)
            );
            return;
        }
        if (session is null || session.ConnectorId != connector.Id)
            throw new InvalidOperationException("Unknown physical session.");
        if (session.EndedAt is not null)
            return;
        if (change.Kind == ChargerEventKind.Measurement)
        {
            var receiptOrder = session.IsDemo && change.UseReceiptOrder;
            if (!receiptOrder && change.ObservedAt < session.StartedAt)
                return;
            if (change.Readings is not null)
                session.LatestReadingsJson = JsonSerializer.Serialize(change.Readings);
            if (
                receiptOrder
                || session.EnergyObservedAt is null
                || change.ObservedAt >= session.EnergyObservedAt
            )
            {
                if (change.SessionEnergyKWh is { } energy && energy >= (session.EnergyKWh ?? 0))
                {
                    session.EnergyKWh = energy;
                    session.EnergyScope = change.EnergyScope;
                    session.EnergyObservedAt = change.ObservedAt;
                }
                else if (change.EnergyScope is not null && session.EnergyObservedAt is null)
                {
                    session.EnergyKWh = null;
                    session.EnergyScope = change.EnergyScope;
                }
            }
            if (receiptOrder || change.ObservedAt >= session.LastMeasurementAt)
            {
                session.LastMeasurementAt = change.ObservedAt;
                session.LastMeasurementReceivedAt = now;
            }
            if (
                change.PowerKW is { } power
                && (
                    receiptOrder
                    || session.PowerObservedAt is null
                    || change.ObservedAt >= session.PowerObservedAt
                )
                && (
                    receiptOrder
                    || connector.StatusObservedAt is null
                    || change.ObservedAt >= connector.StatusObservedAt
                )
            )
            {
                session.PowerKW = power;
                session.PowerObservedAt = change.ObservedAt;
                session.PowerReceivedAt = now;
                session.PowerScope = change.PowerScope;
            }
            if (
                change.StateOfCharge is { } soc
                && (
                    receiptOrder
                    || session.SocObservedAt is null
                    || change.ObservedAt >= session.SocObservedAt
                )
            )
            {
                session.StateOfCharge = soc;
                session.SocObservedAt = change.ObservedAt;
                session.SocReceivedAt = now;
            }
            if (
                change.TemperatureCelsius is { } temperature
                && (
                    receiptOrder
                    || session.TemperatureObservedAt is null
                    || change.ObservedAt >= session.TemperatureObservedAt
                )
            )
            {
                session.TemperatureCelsius = temperature;
                session.TemperatureObservedAt = change.ObservedAt;
            }
            session.Version++;
            await NotifySessionAsync(session, "SessionMeasured", now, ct);
        }
        else if (change.Kind == ChargerEventKind.Stopped)
        {
            if (change.ObservedAt < session.StartedAt)
                throw new InvalidOperationException("End precedes physical start.");
            session.EndedAt = change.ObservedAt;
            session.StopReceivedAt = now;
            session.StopReason = change.StopReason;
            session.CompletionWarning = change.CompletionWarning;
            session.EnergyKWh = change.SessionEnergyKWh;
            session.EnergyScope = "Total";
            session.PowerKW = 0;
            session.Status = ChargingState.Completed;
            session.Version++;
            var claim = await db.ChargingConnectionRequests.SingleOrDefaultAsync(
                x => x.SessionId == session.Id,
                ct
            );
            ChargingRealtime.StageOperations(
                db,
                session.PointId,
                "SessionCompleted",
                "ChargingSession",
                session.Id,
                session.Version,
                now
            );
            DeviceAudit(
                session.PointId,
                "ChargingCompleted",
                new
                {
                    session.Id,
                    session.EndedAt,
                    session.EnergyKWh,
                    session.CalculatedCost,
                    session.StopReason,
                },
                now
            );
            await operators.EnqueueAsync(
                session.PointId,
                $"charging-completed:{session.Id}",
                NotificationKind.ChargingCompleted,
                ct
            );
            if (claim is not null)
            {
                claim.State = "Completed";
                await notifications.EnqueueAsync(
                    claim.DriverId,
                    $"charging-completed:{session.Id}",
                    NotificationKind.ChargingCompleted,
                    ct
                );
                if (session.EnergyKWh is { } energy && session.CalculatedCost is { } cost)
                    db.ChargingHistory.Add(
                        new ChargingHistory
                        {
                            DriverId = claim.DriverId,
                            StationId = session.PointId,
                            StartedAt = session.StartedAt,
                            EndedAt = session.EndedAt.Value,
                            EnergyKWh = energy,
                            UnitPriceSnapshot = session.UnitPriceSnapshot,
                            TotalCost = cost,
                            Currency = session.Currency,
                            IsDemo = session.IsDemo,
                        }
                    );
                ChargingRealtime.Stage(
                    db,
                    session.PointId,
                    "SessionCompleted",
                    "ChargingSession",
                    session.Id,
                    session.Version,
                    now,
                    claim.DriverId,
                    session: ChargingRealtime.SessionDto(
                        session,
                        connector,
                        await db.Stations.SingleAsync(x => x.Id == session.PointId, ct),
                        now
                    )
                );
            }
        }
        await projection.RefreshAsync(session.PointId, ct);
    }

    private async Task NotifySessionAsync(
        ChargingSession session,
        string eventType,
        DateTimeOffset now,
        CancellationToken ct
    )
    {
        ChargingRealtime.StageOperations(
            db,
            session.PointId,
            eventType,
            "ChargingSession",
            session.Id,
            session.Version,
            now
        );
        var claim =
            db.ChargingConnectionRequests.Local.SingleOrDefault(x => x.SessionId == session.Id)
            ?? await db.ChargingConnectionRequests.SingleOrDefaultAsync(
                x => x.SessionId == session.Id,
                ct
            );
        if (claim is not null)
        {
            var connector =
                db.ChargingConnectors.Local.SingleOrDefault(x => x.Id == session.ConnectorId)
                ?? await db.ChargingConnectors.SingleAsync(x => x.Id == session.ConnectorId, ct);
            var station =
                db.Stations.Local.SingleOrDefault(x => x.Id == session.PointId)
                ?? await db.Stations.SingleAsync(x => x.Id == session.PointId, ct);
            ChargingRealtime.Stage(
                db,
                session.PointId,
                eventType,
                "ChargingSession",
                session.Id,
                session.Version,
                now,
                claim.DriverId,
                session: ChargingRealtime.SessionDto(session, connector, station, now)
            );
        }
    }

    private void DeviceAudit(Guid stationId, string action, object after, DateTimeOffset now) =>
        db.Audit.Add(
            new EVCharging.Domain.Auditing.AuditEntry
            {
                ActorId = Guid.Empty,
                ActorRole = "Device",
                EntityId = stationId,
                Action = action,
                ChangesJson = JsonSerializer.Serialize(
                    new { after },
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)
                ),
                RecordedAt = now,
            }
        );
}
