using EVCharging.Domain.Charging;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EVCharging.Infrastructure.Charging.Ocpp;

public sealed class OcppSimulatorProvisioner(
    ChargingDbContext db,
    IOptions<OcppOptions> options,
    OcppStationProjection projection,
    TimeProvider clock
)
{
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var settings = options.Value;
        if (settings.SimulatorEnabled)
            foreach (var identity in settings.SimulatorIdentities)
                await EnsureSimulatorAsync(identity, ct);
        var deviceIds = await db.OcppChargePoints.AsNoTracking().Select(x => x.Id).ToListAsync(ct);
        foreach (var deviceId in deviceIds)
            await ReconcileStartupAsync(deviceId, ct);
    }

    private async Task ReconcileStartupAsync(Guid deviceId, CancellationToken ct)
    {
        // Use the gateway's device lock and fresh entities: an old instance can
        // still receive frames during a rolling deploy. Retry a competing station edit.
        for (var attempt = 0; ; attempt++)
        {
            db.ChangeTracker.Clear();
            await using var transaction = await OcppDeviceUpdate.BeginAsync(db, deviceId, ct);
            var device = await db.OcppChargePoints.SingleOrDefaultAsync(x => x.Id == deviceId, ct);
            if (device is null)
                return;
            var connections = await db
                .OcppConnections.Where(x => x.ChargePointId == deviceId && x.DisconnectedAt == null)
                .ToListAsync(ct);
            foreach (var connection in connections)
                connection.DisconnectedAt = clock.GetUtcNow();
            device.LastSeenAt = null;
            await projection.InvalidateDeviceAsync(device.Id, ct);
            try
            {
                await db.SaveChangesAsync(ct);
                if (transaction is not null)
                    await transaction.CommitAsync(ct);
                return;
            }
            catch (DbUpdateConcurrencyException) when (attempt < 2)
            {
                if (transaction is not null)
                    await transaction.RollbackAsync(ct);
            }
        }
    }

    public async Task<OcppChargePoint> EnsureSimulatorAsync(string identity, CancellationToken ct)
    {
        var station = await db.Stations.SingleOrDefaultAsync(x => x.Code == identity, ct);
        if (station is not null && (!station.IsDemo || station.IsArchived))
            throw new InvalidOperationException(
                "The simulator identity belongs to a non-demo or archived station."
            );

        if (station is null)
        {
            station = new ChargingStation
            {
                Code = identity,
                Name = "Simulator OCPP · " + identity,
                City = "Tiranë",
                Address = "Pikë virtuale për testin OCPP",
                Latitude =
                    41.32
                    + System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes(identity)
                    )[0] / 20000d,
                Longitude =
                    19.81
                    + System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes(identity)
                    )[1] / 20000d,
                ConnectorType = "Type 2",
                ConnectorCount = 1,
                PowerKW = 22,
                PricePerKWh = 0.35m,
                Status = PointState.Unknown,
                StatusSource = "Device",
                IsDemo = true,
            };
            db.Stations.Add(station);
        }

        var device = await db.OcppChargePoints.SingleOrDefaultAsync(
            x => x.Identity == identity,
            ct
        );
        if (device is null)
        {
            if (await db.OcppChargePoints.CountAsync(ct) >= 50)
                throw new InvalidOperationException("The demo charge point limit is reached.");
            device = new OcppChargePoint { Identity = identity };
            db.OcppChargePoints.Add(device);
        }
        device.IsEnabled = true;

        var connector = await db.OcppConnectors.SingleOrDefaultAsync(
            x => x.ChargePointId == device.Id && x.ConnectorNumber == 1,
            ct
        );
        if (connector is null)
        {
            connector = new OcppConnector
            {
                ChargePointId = device.Id,
                ConnectorNumber = 1,
                StationId = station.Id,
            };
            db.OcppConnectors.Add(connector);
        }
        else if (connector.StationId != station.Id)
            throw new InvalidOperationException(
                "The simulator connector is mapped to a different station."
            );

        if (connector.BusinessConnectorId is null)
        {
            var business = new ChargingConnector
            {
                Id = connector.Id,
                StationId = station.Id,
                Number = 1,
            };
            db.ChargingConnectors.Add(business);
            connector.BusinessConnectorId = business.Id;
        }
        await db.SaveChangesAsync(ct);
        return device;
    }
}
