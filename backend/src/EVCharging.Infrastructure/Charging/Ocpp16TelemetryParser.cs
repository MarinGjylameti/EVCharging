using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using EVCharging.Domain.Charging;

namespace EVCharging.Infrastructure.Charging;

public sealed record OcppTelemetry(
    string DeviceId,
    string EventId,
    string Action,
    int? ConnectorId,
    int? TransactionId,
    DateTimeOffset ObservedAt,
    DateTimeOffset ReceivedAt,
    decimal? MeterRegisterKWh,
    decimal? PowerKW,
    PointState? ReportedState,
    string? UnverifiedIdTag,
    string? Reason
);

public sealed class OcppPayloadException(string message) : Exception(message);

/// <summary>
/// Interprets telemetry only. Does not authenticate chargers, accept idTags, allocate transaction IDs,
/// persist inbox records, or expose a production WebSocket server.
/// </summary>
public static partial class Ocpp16TelemetryParser
{
    [GeneratedRegex(@"^[A-Za-z0-9._:-]{1,100}$")]
    private static partial Regex DevicePattern();

    [GeneratedRegex(@"(Z|[+-]\d{2}:\d{2})$")]
    private static partial Regex OffsetPattern();

    public static IReadOnlyList<OcppTelemetry> ParseCall(
        string deviceId,
        string frame,
        DateTimeOffset receivedAt
    )
    {
        if (!DevicePattern().IsMatch(deviceId) || frame.Length > 131072)
            throw new OcppPayloadException("Invalid device or frame size.");
        try
        {
            using var document = JsonDocument.Parse(
                frame,
                new JsonDocumentOptions { MaxDepth = 32 }
            );
            var root = document.RootElement;
            if (
                root.ValueKind != JsonValueKind.Array
                || root.GetArrayLength() != 4
                || root[0].GetInt32() != 2
            )
                throw new OcppPayloadException("Expected an OCPP CALL frame.");
            var messageId = root[1].GetString();
            if (string.IsNullOrWhiteSpace(messageId) || messageId.Length > 36)
                throw new OcppPayloadException("Invalid message ID.");
            var action = root[2].GetString() ?? "";
            var payload = root[3];
            if (payload.ValueKind != JsonValueKind.Object)
                throw new OcppPayloadException("Expected an object payload.");
            var eventId = deviceId + ":" + messageId;
            var at = OptionalString(payload, "timestamp") is { } timestamp
                ? Time(timestamp)
                : receivedAt;
            if (at > receivedAt.AddMinutes(5))
                throw new OcppPayloadException("Device timestamp is in the future.");
            return action switch
            {
                "StatusNotification" =>
                [
                    new(
                        deviceId,
                        eventId,
                        action,
                        Integer(payload, "connectorId", 0),
                        null,
                        at,
                        receivedAt,
                        null,
                        null,
                        Status(RequiredString(payload, "status")),
                        null,
                        RequiredString(payload, "errorCode")
                    ),
                ],
                "StartTransaction" =>
                [
                    new(
                        deviceId,
                        eventId,
                        action,
                        Integer(payload, "connectorId", 1),
                        null,
                        Time(RequiredString(payload, "timestamp")),
                        receivedAt,
                        WholeWh(payload, "meterStart"),
                        null,
                        PointState.Charging,
                        IdTag(payload),
                        null
                    ),
                ],
                "StopTransaction" =>
                [
                    new(
                        deviceId,
                        eventId,
                        action,
                        null,
                        Integer(payload, "transactionId", 0),
                        Time(RequiredString(payload, "timestamp")),
                        receivedAt,
                        WholeWh(payload, "meterStop"),
                        null,
                        null,
                        OptionalString(payload, "idTag"),
                        OptionalString(payload, "reason")
                    ),
                ],
                "MeterValues" => MeterValues(deviceId, eventId, payload, receivedAt),
                _ => throw new OcppPayloadException(
                    "This action is not supported by the telemetry interpreter."
                ),
            };
        }
        catch (Exception ex)
            when (ex
                    is JsonException
                        or InvalidOperationException
                        or KeyNotFoundException
                        or FormatException
                        or OverflowException
            )
        {
            throw new OcppPayloadException("Malformed OCPP telemetry.");
        }
    }

    public static PointState Status(string status) =>
        status switch
        {
            "Available" => PointState.Available,
            "Charging" => PointState.Charging,
            "Preparing" or "Finishing" or "SuspendedEV" or "SuspendedEVSE" or "Reserved" =>
                PointState.Occupied,
            "Unavailable" or "Faulted" => PointState.OutOfService,
            _ => throw new OcppPayloadException("Unknown OCPP status."),
        };

    private static IReadOnlyList<OcppTelemetry> MeterValues(
        string deviceId,
        string eventId,
        JsonElement payload,
        DateTimeOffset receivedAt
    )
    {
        var connector = Integer(payload, "connectorId", 0);
        int? transaction = payload.TryGetProperty("transactionId", out _)
            ? Integer(payload, "transactionId", 0)
            : null;
        var groups = payload.GetProperty("meterValue");
        if (groups.ValueKind != JsonValueKind.Array || groups.GetArrayLength() is < 1 or > 1000)
            throw new OcppPayloadException("Invalid meter groups.");
        var results = new List<OcppTelemetry>();
        var index = 0;
        foreach (var group in groups.EnumerateArray())
        {
            var timestamp = Time(RequiredString(group, "timestamp"));
            if (timestamp > receivedAt.AddMinutes(5))
                throw new OcppPayloadException("Device timestamp is in the future.");
            decimal? energy = null,
                power = null;
            var samples = group.GetProperty("sampledValue");
            if (samples.ValueKind != JsonValueKind.Array || samples.GetArrayLength() == 0)
                throw new OcppPayloadException("Missing samples.");
            foreach (var sample in samples.EnumerateArray())
            {
                // Never mix whole-meter values with per-phase values or measurements from another location.
                if (sample.TryGetProperty("phase", out _))
                    continue;
                if (OptionalString(sample, "location") is { } location && location != "Outlet")
                    continue;
                var measurand =
                    OptionalString(sample, "measurand") ?? "Energy.Active.Import.Register";
                if (measurand is not ("Energy.Active.Import.Register" or "Power.Active.Import"))
                    continue;
                if (OptionalString(sample, "format") is { } format && format != "Raw")
                    throw new OcppPayloadException(
                        "Signed samples require a verified vendor adapter."
                    );
                if (
                    !decimal.TryParse(
                        RequiredString(sample, "value"),
                        NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture,
                        out var value
                    )
                    || value < 0
                )
                    throw new OcppPayloadException("Invalid numeric sample.");
                var unit = OptionalString(sample, "unit") ?? "Wh";
                if (measurand == "Energy.Active.Import.Register")
                {
                    if (energy is not null)
                        throw new OcppPayloadException("Ambiguous aggregate energy samples.");
                    energy = unit switch
                    {
                        "Wh" => value / 1000m,
                        "kWh" => value,
                        _ => throw new OcppPayloadException("Unsupported energy unit."),
                    };
                }
                else
                {
                    if (power is not null)
                        throw new OcppPayloadException("Ambiguous aggregate power samples.");
                    power = unit switch
                    {
                        "W" => value / 1000m,
                        "kW" => value,
                        _ => throw new OcppPayloadException("Unsupported power unit."),
                    };
                }
            }
            results.Add(
                new(
                    deviceId,
                    eventId + ":" + index++,
                    "MeterValues",
                    connector,
                    transaction,
                    timestamp,
                    receivedAt,
                    energy,
                    power,
                    null,
                    null,
                    null
                )
            );
        }
        return results;
    }

    private static int Integer(JsonElement value, string property, int minimum)
    {
        var number = value.GetProperty(property).GetInt32();
        if (number < minimum)
            throw new OcppPayloadException("Invalid integer field.");
        return number;
    }

    private static decimal WholeWh(JsonElement payload, string field)
    {
        var value = payload.GetProperty(field).GetInt64();
        if (value < 0)
            throw new OcppPayloadException("Invalid meter register.");
        return value / 1000m;
    }

    private static string IdTag(JsonElement payload)
    {
        var tag = RequiredString(payload, "idTag");
        if (tag.Length > 20)
            throw new OcppPayloadException("Invalid idTag.");
        return tag;
    }

    private static DateTimeOffset Time(string value)
    {
        if (
            !OffsetPattern().IsMatch(value)
            || !DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var time
            )
        )
            throw new OcppPayloadException("Timestamp must include an offset.");
        return time.ToUniversalTime();
    }

    private static string RequiredString(JsonElement value, string property) =>
        OptionalString(value, property) is { Length: > 0 } text
            ? text
            : throw new OcppPayloadException("Missing string field.");

    private static string? OptionalString(JsonElement value, string property) =>
        value.TryGetProperty(property, out var field) ? field.GetString() : null;
}
