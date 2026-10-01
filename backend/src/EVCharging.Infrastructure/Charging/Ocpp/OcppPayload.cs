using System.Globalization;
using System.Text.Json;

namespace EVCharging.Infrastructure.Charging.Ocpp;

internal sealed class OcppFault(string code, string description) : Exception(description)
{
    public string Code { get; } = code;
}

internal static class OcppPayload
{
    public static string Text(JsonElement payload, string name, int maximum = 100)
    {
        var value = OptionalText(payload, name, maximum);
        if (string.IsNullOrWhiteSpace(value))
            throw new OcppFault("FormationViolation", "Missing or empty " + name + ".");
        return value;
    }

    public static string? OptionalText(JsonElement payload, string name, int maximum = 100)
    {
        if (!payload.TryGetProperty(name, out var value))
            return null;
        if (value.ValueKind != JsonValueKind.String || value.GetString()!.Length > maximum)
            throw new OcppFault("PropertyConstraintViolation", "Invalid " + name + ".");
        return value.GetString();
    }

    public static int Integer(JsonElement payload, string name, int minimum = 0)
    {
        if (
            !payload.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.Number
            || !value.TryGetInt32(out var result)
            || result < minimum
        )
            throw new OcppFault("PropertyConstraintViolation", name + " must be an integer.");
        return result;
    }

    public static DateTimeOffset Timestamp(JsonElement payload, string name = "timestamp")
    {
        var text = Text(payload, name, 40);
        if (
            !DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var result
            ) || !(text.EndsWith('Z') || text.LastIndexOf('+') > 10 || text.LastIndexOf('-') > 10)
        )
            throw new OcppFault("PropertyConstraintViolation", "Invalid UTC timestamp.");
        return result.ToUniversalTime();
    }

    public static IReadOnlyList<ParsedSample> Samples(JsonElement payload, string property)
    {
        if (!payload.TryGetProperty(property, out var values))
            return [];
        if (values.ValueKind != JsonValueKind.Array || values.GetArrayLength() > 64)
            throw new OcppFault("PropertyConstraintViolation", "Too many meter values.");
        var result = new List<ParsedSample>();
        var meterIndex = 0;
        foreach (var meter in values.EnumerateArray())
        {
            if (meter.ValueKind != JsonValueKind.Object)
                throw new OcppFault("FormationViolation", "Invalid meter value.");
            var timestamp = Timestamp(meter);
            if (
                !meter.TryGetProperty("sampledValue", out var samples)
                || samples.ValueKind != JsonValueKind.Array
                || samples.GetArrayLength() is < 1 or > 64
            )
                throw new OcppFault("FormationViolation", "Missing sampled values.");
            var sampleIndex = 0;
            foreach (var sample in samples.EnumerateArray())
            {
                if (sample.ValueKind != JsonValueKind.Object || result.Count >= 256)
                    throw new OcppFault(
                        "PropertyConstraintViolation",
                        "Invalid or excessive samples."
                    );
                var text = Text(sample, "value", 512);
                result.Add(
                    new ParsedSample(
                        meterIndex,
                        sampleIndex++,
                        timestamp,
                        text,
                        decimal.TryParse(
                            text,
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out var numeric
                        ) && numeric is > -1_000_000_000_000m and < 1_000_000_000_000m
                            ? decimal.Round(numeric, 8)
                            : null,
                        OptionalText(sample, "measurand", 80) ?? "Energy.Active.Import.Register",
                        OptionalText(sample, "unit", 20),
                        OptionalText(sample, "phase", 20),
                        OptionalText(sample, "location", 30),
                        OptionalText(sample, "context", 30),
                        OptionalText(sample, "format", 20)
                    )
                );
            }
            meterIndex++;
        }
        return result;
    }
}

internal sealed record ParsedSample(
    int MeterIndex,
    int SampleIndex,
    DateTimeOffset Timestamp,
    string Text,
    decimal? Numeric,
    string Measurand,
    string? Unit,
    string? Phase,
    string? Location,
    string? Context,
    string? Format
);
