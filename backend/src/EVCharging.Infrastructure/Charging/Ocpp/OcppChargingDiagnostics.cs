using System.Text.Json;
using EVCharging.Application.Charging;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EVCharging.Infrastructure.Charging.Ocpp;

public sealed class OcppChargingDiagnostics(ChargingDbContext db) : IChargingDiagnostics
{
    public async Task<ChargingDiagnostics?> GetAsync(
        Guid connectorId,
        Guid? sessionId,
        CancellationToken ct,
        DateTimeOffset? since = null
    )
    {
        var connector = await db
            .OcppConnectors.AsNoTracking()
            .SingleOrDefaultAsync(x => x.BusinessConnectorId == connectorId, ct);
        if (connector is null)
            return null;
        var transaction = sessionId is null
            ? null
            : await db
                .OcppChargingTransactions.AsNoTracking()
                .SingleOrDefaultAsync(x => x.SessionId == sessionId, ct);
        var singleConnector =
            await db.OcppConnectors.CountAsync(x => x.ChargePointId == connector.ChargePointId, ct)
            == 1;
        var recent = await db
            .OcppInboundMessages.AsNoTracking()
            .Where(x =>
                x.ChargePointId == connector.ChargePointId
                && (since == null || x.ReceivedAt >= since)
                && (
                    transaction == null
                    || transaction.StopReceivedAt == null
                    || x.ReceivedAt <= transaction.StopReceivedAt
                )
            )
            .OrderByDescending(x => x.CommandVersion)
            .ThenByDescending(x => x.ReceivedAt)
            .Take(64)
            .ToListAsync(ct);
        ChargingCommandResult? last = null;
        ChargingCommandResult? failure = null;
        var acceptedActions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var message in recent)
        {
            if (message.ResponseJson is null)
                continue;
            using var frame = JsonDocument.Parse(message.FrameJson);
            var payload = frame.RootElement[3];
            var belongs =
                !payload.TryGetProperty("connectorId", out var number)
                || number.ValueKind == JsonValueKind.Number
                    && number.TryGetInt32(out var parsedNumber)
                    && (parsedNumber == connector.ConnectorNumber || parsedNumber == 0);
            if (message.Action == "StopTransaction")
                belongs =
                    singleConnector
                        && transaction?.StoppedAt is null
                        && message.ProcessingState == "Rejected"
                    || transaction is not null
                        && payload.TryGetProperty("transactionId", out var id)
                        && id.ValueKind == JsonValueKind.Number
                        && id.TryGetInt32(out var parsedId)
                        && parsedId == transaction.Ocpp16TransactionId;
            if (
                payload.TryGetProperty("transactionId", out var meterTransaction)
                && message.Action == "MeterValues"
                && transaction is not null
            )
                belongs =
                    belongs
                    && meterTransaction.ValueKind == JsonValueKind.Number
                    && meterTransaction.TryGetInt32(out var meterId)
                    && meterId == transaction.Ocpp16TransactionId;
            // Historical owners only see commands belonging to their own transaction.
            if (transaction?.StoppedAt is not null)
                belongs =
                    message.Id == transaction.StartMessageId
                    || message.Id == transaction.StopMessageId;
            if (!belongs)
                continue;
            var command = OcppGateway.CommandResult(message);
            last ??= command;
            if (command.Accepted)
                acceptedActions.Add(command.Action);
            else if (failure is null && !acceptedActions.Contains(command.Action))
                failure = command;
        }
        var sampleQuery = db
            .OcppMeterSamples.AsNoTracking()
            .Where(x =>
                x.ChargePointId == connector.ChargePointId
                && x.ConnectorNumber == connector.ConnectorNumber
                && (since == null || x.ReceivedAt >= since)
            );
        if (transaction is not null)
            sampleQuery = sampleQuery.Where(x => x.TransactionId == transaction.Id);
        else
            sampleQuery = sampleQuery.Where(x => x.TransactionId == null);
        var samples = await sampleQuery
            .OrderByDescending(x => x.ReceivedAt)
            .ThenByDescending(x => x.MeterValueIndex)
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
            .ToListAsync(ct);
        return new(
            transaction?.Ocpp16TransactionId.ToString(),
            transaction?.MeterStartWh,
            transaction?.StopReason,
            last,
            samples,
            failure
        );
    }
}
