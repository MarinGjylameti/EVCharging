using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EVCharging.Application.Charging;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace EVCharging.Infrastructure.Charging.Ocpp;

public sealed class OcppGateway(
    ChargingDbContext db,
    OcppActionHandler actions,
    OcppDeviceAuthentication authentication,
    OcppStationProjection projection,
    StationEventSignal events,
    TimeProvider clock,
    IOptions<OcppOptions> options
) : IOcppGateway
{
    public Task<Guid?> AuthenticateAsync(string identity, string username, string password, CancellationToken ct) =>
        authentication.AuthenticateAsync(identity, username, password, ct);
    public async Task<Guid> OpenAsync(Guid chargePointId, CancellationToken ct)
    {
        await using var transaction = await OcppDeviceUpdate.BeginAsync(db, chargePointId, ct);
        var device = await db.OcppChargePoints.SingleAsync(x => x.Id == chargePointId, ct);
        device.LastSeenAt = clock.GetUtcNow();
        var abandoned = await db.OcppConnections.Where(x => x.ChargePointId == chargePointId && x.DisconnectedAt == null).ToListAsync(ct);
        foreach (var old in abandoned)
        {
            old.DisconnectedAt = clock.GetUtcNow();
            old.CloseReason = "Superseded by a new device connection";
        }
        var connection = new OcppConnection
        {
            ChargePointId = chargePointId,
            ConnectedAt = clock.GetUtcNow(),
        };
        db.OcppConnections.Add(connection);
        await projection.InvalidateDeviceAsync(chargePointId, ct);
        await projection.ConnectedAsync(chargePointId, ct);
        await db.SaveChangesAsync(ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);
        events.Notify();
        return connection.Id;
    }

    public async Task CloseAsync(
        Guid chargePointId,
        Guid connectionId,
        int? closeCode,
        CancellationToken ct
    )
    {
        await using var transaction = await OcppDeviceUpdate.BeginAsync(db, chargePointId, ct);
        var connection = await db.OcppConnections.SingleAsync(x => x.Id == connectionId, ct);
        if (connection.DisconnectedAt is not null) return;
        connection.DisconnectedAt = clock.GetUtcNow();
        connection.CloseCode = closeCode;
        var device = await db.OcppChargePoints.SingleAsync(x => x.Id == chargePointId, ct);
        device.LastSeenAt = null;
        await projection.InvalidateDeviceAsync(chargePointId, ct);
        await db.SaveChangesAsync(ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);
        events.Notify();
    }

    public async Task<string> ProcessAsync(
        Guid chargePointId,
        Guid connectionId,
        string frame,
        CancellationToken ct
    )
    {
        using var json = JsonDocument.Parse(frame, new JsonDocumentOptions { MaxDepth = 16 });
        var root = json.RootElement;
        if (
            root.ValueKind != JsonValueKind.Array
            || root.GetArrayLength() != 4
            || root[0].ValueKind != JsonValueKind.Number
            || !root[0].TryGetInt32(out var type)
            || type != 2
            || root[1].ValueKind != JsonValueKind.String
            || root[1].GetString()!.Length is < 1 or > 100
            || root[2].ValueKind != JsonValueKind.String
            || root[2].GetString()!.Length is < 1 or > 60
            || root[3].ValueKind != JsonValueKind.Object
        )
            throw new JsonException("Expected an OCPP 1.6 CALL frame.");

        var callId = root[1].GetString()!;
        var action = root[2].GetString()!;
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(root)))
        );
        IDbContextTransaction? transaction = await OcppDeviceUpdate.BeginAsync(
            db,
            chargePointId,
            ct
        );
        try
        {
            var device = await db.OcppChargePoints.SingleAsync(x => x.Id == chargePointId, ct);
            if (!device.IsEnabled)
                throw new OcppFault("SecurityError", "The charge point is disabled.");
            var previous = await db.OcppInboundMessages.SingleOrDefaultAsync(
                x => x.ConnectionId == connectionId && x.CallId == callId,
                ct
            );
            if (previous is null && action is ("StartTransaction" or "StopTransaction"))
                previous = await db
                    .OcppInboundMessages.Where(x =>
                        x.ChargePointId == chargePointId
                        && x.CallId == callId
                        && x.PayloadHash == hash
                        && x.Action == action
                        && x.ReceivedAt >= clock.GetUtcNow().AddDays(-1)
                    )
                    .OrderByDescending(x => x.ReceivedAt)
                    .FirstOrDefaultAsync(ct);
            var wasOffline = device.LastSeenAt is null || device.LastSeenAt < clock.GetUtcNow().AddSeconds(-options.Value.OfflineSeconds);
            device.LastSeenAt = clock.GetUtcNow();
            var connection = await db.OcppConnections.SingleAsync(
                x => x.Id == connectionId && x.ChargePointId == chargePointId,
                ct
            );
            connection.LastFrameAt = clock.GetUtcNow();
            if (connection.DisconnectedAt is not null)
                throw new OcppFault("SecurityError", "The connection was superseded.");
            if (wasOffline) await projection.ConnectedAsync(chargePointId, ct);
            if (previous is not null)
            {
                var cached =
                    previous.PayloadHash == hash && previous.ResponseJson is not null
                        ? previous.ResponseJson
                        : Error(
                            callId,
                            "ProtocolError",
                            "The call ID was reused with different content."
                        );
                await db.SaveChangesAsync(ct);
                if (transaction is not null)
                    await transaction.CommitAsync(ct);
                return cached;
            }

            var message = Message(chargePointId, connectionId, callId, action, frame, hash);
            db.OcppInboundMessages.Add(message);
            var result = await actions.HandleAsync(device, message, root[3], ct);
            var response = JsonSerializer.Serialize(new object[] { 3, callId, result });
            message.ResponseJson = response;
            message.ProcessingState = "Processed";
            message.ProcessedAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
            if (transaction is not null)
                await transaction.CommitAsync(ct);
            if (
                action
                is "BootNotification"
                    or "StatusNotification"
                    or "StartTransaction"
                    or "StopTransaction"
                    or "MeterValues"
            )
                events.Notify();
            return response;
        }
        catch (OcppFault fault)
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(ct);
                await transaction.DisposeAsync();
                transaction = null;
            }
            db.ChangeTracker.Clear();
            var response = Error(callId, fault.Code, fault.Message);
            var message = Message(chargePointId, connectionId, callId, action, frame, hash);
            message.ResponseJson = response;
            message.ProcessingState = "Rejected";
            message.FailureCode = fault.Code;
            message.ProcessedAt = clock.GetUtcNow();
            db.OcppInboundMessages.Add(message);
            await db.SaveChangesAsync(ct);
            return response;
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
        }
    }

    private OcppInboundMessage Message(
        Guid deviceId,
        Guid connectionId,
        string callId,
        string action,
        string frame,
        string hash
    ) =>
        new()
        {
            ChargePointId = deviceId,
            ConnectionId = connectionId,
            CallId = callId,
            Action = action,
            FrameJson = frame,
            PayloadHash = hash,
            ReceivedAt = clock.GetUtcNow(),
        };

    private static string Error(string id, string code, string description) =>
        JsonSerializer.Serialize(new object[] { 4, id, code, description, new { } });
}

