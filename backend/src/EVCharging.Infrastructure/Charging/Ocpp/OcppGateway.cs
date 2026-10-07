using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EVCharging.Application.Charging;
using EVCharging.Application.Common;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EVCharging.Infrastructure.Charging.Ocpp;

public sealed class OcppGateway(
    ChargingDbContext db,
    OcppActionHandler actions,
    OcppDeviceAuthentication authentication,
    OcppStationProjection projection,
    StationEventSignal events,
    TimeProvider clock,
    IOptions<OcppOptions> options,
    ILogger<OcppGateway> logger,
    IApplicationDiagnostics diagnostics
) : IOcppGateway
{
    public Task<bool> IsConnectionCurrentAsync(
        Guid chargePointId,
        Guid connectionId,
        CancellationToken ct
    ) =>
        db
            .OcppConnections.AsNoTracking()
            .AnyAsync(
                c =>
                    c.Id == connectionId
                    && c.ChargePointId == chargePointId
                    && c.DisconnectedAt == null
                    && db.OcppChargePoints.Any(d =>
                        d.Id == chargePointId
                        && d.IsEnabled
                        && d.CredentialVersion == c.CredentialVersion
                    ),
                ct
            );

    public Task<Guid?> AuthenticateAsync(
        string identity,
        string username,
        string password,
        CancellationToken ct
    ) => authentication.AuthenticateAsync(identity, username, password, ct);

    public async Task<Guid> OpenAsync(Guid chargePointId, CancellationToken ct)
    {
        await using var transaction = await OcppDeviceUpdate.BeginAsync(db, chargePointId, ct);
        var device = await db.OcppChargePoints.SingleAsync(x => x.Id == chargePointId, ct);
        if (
            !device.IsEnabled
            || authentication.AuthenticatedCredentialVersion is { } admission
                && admission != device.CredentialVersion
            || device.ProvisionedSerialNumber is not null
                && authentication.AuthenticatedCredentialVersion is null
        )
            throw new RequestFailure(
                401,
                "device_access_changed",
                "The device connection permission changed. Reconnect if enabled."
            );
        device.LastSeenAt = clock.GetUtcNow();
        var abandoned = await db
            .OcppConnections.Where(x =>
                x.ChargePointId == chargePointId && x.DisconnectedAt == null
            )
            .ToListAsync(ct);
        foreach (var old in abandoned)
        {
            old.DisconnectedAt = clock.GetUtcNow();
            old.CloseReason = "Superseded by a new device connection";
        }
        var connection = new OcppConnection
        {
            ChargePointId = chargePointId,
            ConnectedAt = clock.GetUtcNow(),
            CredentialVersion = device.CredentialVersion,
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
        CancellationToken ct,
        string? closeReason = null
    )
    {
        await using var transaction = await OcppDeviceUpdate.BeginAsync(db, chargePointId, ct);
        var connection = await db.OcppConnections.SingleAsync(x => x.Id == connectionId, ct);
        if (connection.DisconnectedAt is not null)
            return;
        connection.DisconnectedAt = clock.GetUtcNow();
        connection.CloseCode = closeCode;
        connection.CloseReason = closeReason;
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
        var started = Stopwatch.GetTimestamp();
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(root)))
        );
        IDbContextTransaction? transaction = await OcppDeviceUpdate.BeginAsync(
            db,
            chargePointId,
            ct
        );
        string? chargePointIdentity = null;
        try
        {
            var device = await db.OcppChargePoints.SingleAsync(x => x.Id == chargePointId, ct);
            chargePointIdentity = device.Identity;
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
            var wasOffline =
                device.LastSeenAt is null
                || device.LastSeenAt < clock.GetUtcNow().AddSeconds(-options.Value.OfflineSeconds);
            device.LastSeenAt = clock.GetUtcNow();
            var connection = await db.OcppConnections.SingleAsync(
                x => x.Id == connectionId && x.ChargePointId == chargePointId,
                ct
            );
            connection.LastFrameAt = clock.GetUtcNow();
            if (connection.CredentialVersion != device.CredentialVersion)
                throw new OcppFault(
                    "SecurityError",
                    "The device connection permission was revoked."
                );
            if (connection.DisconnectedAt is not null)
                throw new OcppFault("SecurityError", "The connection was superseded.");
            if (wasOffline)
                await projection.ConnectedAsync(chargePointId, ct);
            if (previous is not null)
            {
                var reused = previous.PayloadHash != hash || previous.ResponseJson is null;
                var cached = !reused
                    ? previous.ResponseJson
                    : Error(
                        callId,
                        "ProtocolError",
                        "The call ID was reused with different content."
                    );
                if (reused)
                    await StageCommandAsync(
                        chargePointId,
                        root[3],
                        previous,
                        new(
                            action,
                            false,
                            "ProtocolError",
                            "The call ID was reused with different content.",
                            clock.GetUtcNow()
                        ),
                        ct
                    );
                await db.SaveChangesAsync(ct);
                if (transaction is not null)
                    await transaction.CommitAsync(ct);
                events.Notify();
                using var replayContext = diagnostics.BeginScope(MessageContext(previous, root[3]));
                logger.LogInformation(
                    new EventId(1101, "message_replayed"),
                    "OCPP action {Action} replayed for {Identity}; reusedCallId={Reused}; call={CallId}; connection={ConnectionId}.",
                    action,
                    device.Identity,
                    reused,
                    callId,
                    connectionId
                );
                return cached!;
            }

            var message = Message(chargePointId, connectionId, callId, action, frame, hash);
            db.OcppInboundMessages.Add(message);
            var result = await actions.HandleAsync(device, message, root[3], ct);
            var response = JsonSerializer.Serialize(new object[] { 3, callId, result });
            message.ResponseJson = response;
            message.ProcessingState = "Processed";
            message.ProcessedAt = clock.GetUtcNow();
            await StageCommandAsync(chargePointId, root[3], message, CommandResult(message), ct);
            await db.SaveChangesAsync(ct);
            if (transaction is not null)
                await transaction.CommitAsync(ct);
            events.Notify();
            using var committedContext = diagnostics.BeginScope(MessageContext(message, root[3]));
            logger.Log(
                action == "MeterValues" ? LogLevel.Debug : LogLevel.Information,
                new EventId(1102, "message_committed"),
                "OCPP action {Action} processed and committed for {Identity}. call={CallId}; connection={ConnectionId}; elapsedMs={ElapsedMs}.",
                action,
                device.Identity,
                callId,
                connectionId,
                (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds
            );
            if (action == "BootNotification")
                logger.LogInformation(
                    "OCPP BootNotification accepted and committed for {Identity}.",
                    device.Identity
                );
            if (action == "StatusNotification")
                logger.LogInformation(
                    "OCPP status for {Identity}: connector={Connector}, status={Status}, error={ErrorCode}.",
                    device.Identity,
                    root[3].GetProperty("connectorId").GetInt32(),
                    root[3].GetProperty("status").GetString(),
                    root[3].GetProperty("errorCode").GetString()
                );
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
            transaction = await OcppDeviceUpdate.BeginAsync(db, chargePointId, ct);
            var response = Error(callId, fault.Code, fault.Message);
            var existing = await db.OcppInboundMessages.SingleOrDefaultAsync(
                x => x.ConnectionId == connectionId && x.CallId == callId,
                ct
            );
            var message =
                existing ?? Message(chargePointId, connectionId, callId, action, frame, hash);
            if (existing is null)
            {
                message.ResponseJson = response;
                message.ProcessingState = "Rejected";
                message.FailureCode = fault.Code;
                message.ProcessedAt = clock.GetUtcNow();
                db.OcppInboundMessages.Add(message);
            }
            await StageCommandAsync(
                chargePointId,
                root[3],
                message,
                new(action, false, fault.Code, fault.Message, clock.GetUtcNow()),
                ct
            );
            await db.SaveChangesAsync(ct);
            if (transaction is not null)
                await transaction.CommitAsync(ct);
            events.Notify();
            using var rejectedContext = diagnostics.BeginScope(MessageContext(message, root[3]));
            logger.LogWarning(
                new EventId(1103, "message_rejected"),
                "OCPP action {Action} rejected for {Identity}: {FailureCode}. call={CallId}; connection={ConnectionId}.",
                action,
                chargePointIdentity,
                fault.Code,
                callId,
                connectionId
            );
            return response;
        }
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync();
        }
    }

    internal static ChargingCommandResult CommandResult(OcppInboundMessage message)
    {
        using var response = JsonDocument.Parse(message.ResponseJson!);
        var root = response.RootElement;
        if (message.ProcessingState == "Rejected")
            return new(
                message.Action,
                false,
                message.FailureCode,
                root[3].GetString(),
                message.ReceivedAt,
                message.CommandVersion
            );
        if (
            root[2].TryGetProperty("idTagInfo", out var authorization)
            && authorization.TryGetProperty("status", out var status)
            && status.GetString() is { } value
            && value != "Accepted"
        )
            return new(
                message.Action,
                false,
                "Authorization" + value,
                "The authorization tag was not accepted.",
                message.ReceivedAt,
                message.CommandVersion
            );
        return new(message.Action, true, null, null, message.ReceivedAt, message.CommandVersion);
    }

    // Correlate using entities already loaded while processing; logging adds no
    // database reads to the metering path. Unknown identifiers remain unbound.
    private ApplicationLogContext MessageContext(OcppInboundMessage message, JsonElement payload)
    {
        int? number =
            payload.TryGetProperty("connectorId", out var connector)
            && connector.ValueKind == JsonValueKind.Number
            && connector.TryGetInt32(out var connectorNumber)
            && connectorNumber > 0
                ? connectorNumber
                : null;
        var hasTransaction = payload.TryGetProperty("transactionId", out var transaction);
        int? transactionId =
            hasTransaction
            && transaction.ValueKind == JsonValueKind.Number
            && transaction.TryGetInt32(out var id)
                ? id
                : null;
        var charge = db.OcppChargingTransactions.Local.FirstOrDefault(x =>
            x.ChargePointId == message.ChargePointId
            && (
                x.StartMessageId == message.Id
                || x.StopMessageId == message.Id
                || transactionId is not null && x.Ocpp16TransactionId == transactionId
                || !hasTransaction
                    && number is not null
                    && x.ConnectorNumber == number
                    && x.StoppedAt == null
            )
        );
        number = charge?.ConnectorNumber ?? number;
        var mapped = db.OcppConnectors.Local.FirstOrDefault(x =>
            x.ChargePointId == message.ChargePointId && x.ConnectorNumber == number
        );
        var physicalSession = db.ChargingSessions.Local.FirstOrDefault(x =>
            x.ConnectorId == mapped?.BusinessConnectorId && x.EndedAt == null
        );
        return new(
            InboxMessageId: message.Id,
            SessionId: charge?.SessionId ?? (!hasTransaction ? physicalSession?.Id : null),
            StationId: charge?.StationId ?? mapped?.StationId,
            ConnectorId: mapped?.BusinessConnectorId,
            ConnectorNumber: number
        );
    }

    private async Task StageCommandAsync(
        Guid deviceId,
        JsonElement payload,
        OcppInboundMessage message,
        ChargingCommandResult result,
        CancellationToken ct
    )
    {
        var device = await db.OcppChargePoints.SingleAsync(x => x.Id == deviceId, ct);
        var version = ++device.CommandVersion;
        // Cached original replies retain their original sequence. Conflicting retries
        // still produce a fresh private diagnostic without replacing that reply.
        if (message.CommandVersion == 0)
            message.CommandVersion = version;
        var connectors = await db
            .OcppConnectors.Where(x =>
                x.ChargePointId == deviceId && x.BusinessConnectorId != null && x.StationId != null
            )
            .ToListAsync(ct);
        if (
            payload.TryGetProperty("connectorId", out var connectorNumber)
            && connectorNumber.ValueKind == JsonValueKind.Number
            && connectorNumber.TryGetInt32(out var number)
            && number > 0
        )
            connectors = connectors.Where(x => x.ConnectorNumber == number).ToList();
        OcppChargingTransaction? targetTransaction = null;
        if (
            result.Action is "StopTransaction" or "MeterValues"
            && payload.TryGetProperty("transactionId", out var transactionId)
            && transactionId.ValueKind == JsonValueKind.Number
            && transactionId.TryGetInt32(out var id)
        )
        {
            var transaction = await db.OcppChargingTransactions.SingleOrDefaultAsync(
                x => x.ChargePointId == deviceId && x.Ocpp16TransactionId == id,
                ct
            );
            targetTransaction = transaction;
            if (transaction is not null)
                connectors = connectors
                    .Where(x => x.ConnectorNumber == transaction.ConnectorNumber)
                    .ToList();
            else if (connectors.Count != 1)
                return;
        }
        var readings = db
            .OcppMeterSamples.Local.Where(x => x.MessageId == message.Id)
            .OrderByDescending(x => x.MeterValueIndex)
            .ThenBy(x => x.SampleIndex)
            .Take(64)
            .Select(x => new ChargingSampleDiagnostic(
                x.Measurand,
                x.ValueText,
                x.Unit,
                x.Phase,
                x.Location,
                x.Context,
                x.Format,
                x.ObservedAt,
                x.ReceivedAt
            ))
            .ToArray();
        foreach (var mapped in connectors)
        {
            var claim = await db.ChargingConnectionRequests.SingleOrDefaultAsync(
                x =>
                    x.StationId == mapped.StationId
                    && x.ConnectorNumber == mapped.ConnectorNumber
                    && (
                        x.State == "Linked"
                        || x.State == "Waiting" && x.ExpiresAt > result.ReceivedAt
                    ),
                ct
            );
            if (
                claim is null
                && result.Action == "StopTransaction"
                && targetTransaction?.SessionId is { } stoppedSessionId
            )
                claim = await db.ChargingConnectionRequests.SingleOrDefaultAsync(
                    x => x.SessionId == stoppedSessionId && x.State == "Completed",
                    ct
                );
            Guid? recipient = claim?.DriverId;
            if (recipient is null)
            {
                if (
                    device.AssignedDriverId is { } demoDriver
                    && device.DriverCardIdTag is null
                    && device.DriverAssignmentSince <= result.ReceivedAt
                    && await DeviceDriverAssignment.ForDriverAsync(
                        db,
                        demoDriver,
                        mapped.StationId!.Value,
                        ct
                    )
                        is not null
                )
                    recipient = demoDriver;
            }
            if (recipient is null)
                continue;
            if (targetTransaction is not null && claim?.SessionId != targetTransaction.SessionId)
                continue;
            var connector = await db.ChargingConnectors.SingleAsync(
                x => x.Id == mapped.BusinessConnectorId,
                ct
            );
            ChargingRealtime.Stage(
                db,
                connector.StationId,
                "DeviceCommandChanged",
                "ChargingConnector",
                connector.Id,
                version,
                result.ReceivedAt,
                recipient.Value,
                connector: ChargingRealtime.ConnectorDto(connector),
                command: result with
                {
                    Version = version,
                    Readings = readings.Length > 0 ? readings : null,
                }
            );
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
