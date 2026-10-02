using EVCharging.Application.Charging;
using EVCharging.Domain.Charging;
using EVCharging.Domain.Identity;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EVCharging.Infrastructure.Charging;

// Participates in the adapter's transaction; never commits independently.
// Protocol inbox deduplication happens before normalized events reach this port.
public sealed class ChargerEventReceiver(ChargingDbContext db, StationProjection projection, TimeProvider clock)
    : IChargerEventReceiver
{
    public async Task ReceiveAsync(ChargerEvent change, CancellationToken ct)
    {
        var connector = await db.ChargingConnectors.SingleAsync(x => x.Id == change.ConnectorId, ct);
        var now = change.ReceivedAt ?? clock.GetUtcNow();
        if (change.Kind is ChargerEventKind.Connected or ChargerEventKind.Disconnected or ChargerEventKind.StatusReported)
        {
            var priorStatus = connector.Status;
            var priorOnline = connector.IsOnline;
            connector.IsOnline = change.Kind != ChargerEventKind.Disconnected;
            if (change.Status is { } status) connector.Status = status;
            connector.StatusObservedAt = change.ObservedAt;
            connector.StatusReceivedAt = now;
            connector.Version++;
            var active = await db.ChargingSessions.SingleOrDefaultAsync(x => x.ConnectorId == connector.Id && x.EndedAt == null, ct);
            if (active is { EndedAt: null })
            {
                if (change.Status is { } state) active.Status = state;
                if (priorStatus != connector.Status || priorOnline != connector.IsOnline)
                {
                    active.PowerKW = null;
                    active.PowerReceivedAt = null;
                }
                active.Version++;
                await NotifySessionAsync(active, "SessionStatusChanged", now, ct);
            }
            ChargingRealtime.Stage(db, connector.StationId, "ConnectorChanged", "ChargingConnector",
                connector.Id, connector.Version, now);
            await projection.RefreshAsync(connector.StationId, ct);
            return;
        }

        if (change.SessionId is not { } sessionId) throw new InvalidOperationException("A physical session identity is required.");
        var session = db.ChargingSessions.Local.SingleOrDefault(x => x.Id == sessionId)
            ?? await db.ChargingSessions.SingleOrDefaultAsync(x => x.Id == sessionId, ct);
        if (change.Kind == ChargerEventKind.Started)
        {
            if (session is not null) return;
            var station = await db.Stations.SingleAsync(x => x.Id == connector.StationId, ct);
            session = new ChargingSession(station.Id, station.PricePerKWh, station.Currency, change.ObservedAt)
            {
                Id = sessionId, ConnectorId = connector.Id, ConnectorNumber = connector.Number,
                StartReceivedAt = now, IsDemo = station.IsDemo, Status = connector.Status,
                Version = 1,
            };
            db.ChargingSessions.Add(session);
            var waiting = await db.ChargingConnectionRequests.SingleOrDefaultAsync(x =>
                x.StationId == station.Id && x.ConnectorNumber == connector.Number && x.State == "Waiting", ct);
            if (waiting is not null)
            {
                if (waiting.ExpiresAt <= now) waiting.State = "Expired";
                else if (!await db.Accounts.AnyAsync(x => x.Id == waiting.DriverId && x.IsActive
                    && x.ArchivedAt == null && x.Role == UserRole.Driver, ct)) waiting.State = "Cancelled";
                else
                {
                    waiting.State = "Linked"; waiting.SessionId = session.Id; waiting.LinkedAt = now;
                    ChargingRealtime.Stage(db, station.Id, "SessionStarted", "ChargingSession", session.Id,
                        session.Version, now, waiting.DriverId,
                        session: ChargingRealtime.SessionDto(session, connector, station, now));
                }
            }
            // Also notify open, currently unclaimed charger views without private data.
            ChargingRealtime.Stage(db, station.Id, "ConnectorChanged", "ChargingConnector", connector.Id, connector.Version, now);
            return;
        }
        if (session is null || session.ConnectorId != connector.Id) throw new InvalidOperationException("Unknown physical session.");
        if (session.EndedAt is not null) return;
        if (change.Kind == ChargerEventKind.Measurement)
        {
            if (change.ObservedAt < session.StartedAt) return;
            if (change.ObservedAt >= session.LastMeasurementAt)
            {
                if (change.SessionEnergyKWh is { } energy && energy >= (session.EnergyKWh ?? 0))
                { session.EnergyKWh = energy; session.EnergyScope = change.EnergyScope; }
                else if (change.EnergyScope is not null && session.LastMeasurementReceivedAt is null)
                { session.EnergyKWh = null; session.EnergyScope = change.EnergyScope; }
                session.LastMeasurementAt = change.ObservedAt;
                session.LastMeasurementReceivedAt = now;
            }
            if (change.PowerKW is { } power && (session.PowerObservedAt is null || change.ObservedAt >= session.PowerObservedAt))
            {
                session.PowerKW = power; session.PowerObservedAt = change.ObservedAt;
                session.PowerReceivedAt = now; session.PowerScope = change.PowerScope;
            }
            if (change.StateOfCharge is { } soc && (session.SocObservedAt is null || change.ObservedAt >= session.SocObservedAt))
            { session.StateOfCharge = soc; session.SocObservedAt = change.ObservedAt; }
            if (change.TemperatureCelsius is { } temperature && (session.TemperatureObservedAt is null || change.ObservedAt >= session.TemperatureObservedAt))
            { session.TemperatureCelsius = temperature; session.TemperatureObservedAt = change.ObservedAt; }
            session.Version++;
            await NotifySessionAsync(session, "SessionMeasured", now, ct);
        }
        else if (change.Kind == ChargerEventKind.Stopped)
        {
            if (change.ObservedAt < session.StartedAt) throw new InvalidOperationException("End precedes physical start.");
            session.EndedAt = change.ObservedAt; session.StopReceivedAt = now;
            session.EnergyKWh = change.SessionEnergyKWh; session.EnergyScope = "Total";
            session.PowerKW = 0; session.Status = ChargingState.Completed; session.Version++;
            var claim = await db.ChargingConnectionRequests.SingleOrDefaultAsync(x => x.SessionId == session.Id, ct);
            if (claim is not null)
            {
                claim.State = "Completed";
                if (session.EnergyKWh is { } energy && session.CalculatedCost is { } cost)
                    db.ChargingHistory.Add(new ChargingHistory
                    {
                        DriverId = claim.DriverId, StationId = session.PointId,
                        StartedAt = session.StartedAt, EndedAt = session.EndedAt.Value,
                        EnergyKWh = energy, UnitPriceSnapshot = session.UnitPriceSnapshot,
                        TotalCost = cost, Currency = session.Currency, IsDemo = session.IsDemo,
                    });
                ChargingRealtime.Stage(db, session.PointId, "SessionCompleted", "ChargingSession",
                    session.Id, session.Version, now, claim.DriverId,
                    session: ChargingRealtime.SessionDto(session, connector,
                        await db.Stations.SingleAsync(x => x.Id == session.PointId, ct), now));
            }
        }
    }

    private async Task NotifySessionAsync(ChargingSession session, string eventType, DateTimeOffset now, CancellationToken ct)
    {
        var claim = db.ChargingConnectionRequests.Local.SingleOrDefault(x => x.SessionId == session.Id)
            ?? await db.ChargingConnectionRequests.SingleOrDefaultAsync(x => x.SessionId == session.Id, ct);
        if (claim is not null)
        {
            var connector = db.ChargingConnectors.Local.SingleOrDefault(x => x.Id == session.ConnectorId)
                ?? await db.ChargingConnectors.SingleAsync(x => x.Id == session.ConnectorId, ct);
            var station = db.Stations.Local.SingleOrDefault(x => x.Id == session.PointId)
                ?? await db.Stations.SingleAsync(x => x.Id == session.PointId, ct);
            ChargingRealtime.Stage(db, session.PointId, eventType, "ChargingSession", session.Id,
                session.Version, now, claim.DriverId,
                session: ChargingRealtime.SessionDto(session, connector, station, now));
        }
    }
}
