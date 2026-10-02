using EVCharging.Infrastructure.Persistence;

namespace EVCharging.Infrastructure.Charging.Ocpp;

// Reduce one meter value at a time. An aggregate and its phases are alternatives,
// not quantities to add together. Source readings remain unchanged in the inbox.
internal sealed record OcppMeterReading(
    string Measurand,
    decimal Value,
    string Scope,
    DateTimeOffset ObservedAt,
    DateTimeOffset ReceivedAt
)
{
    public decimal? SessionEnergy(long meterStartWh) =>
        Measurand == "Energy.Active.Import.Register"
        && (Scope == "Total" || meterStartWh == 0)
        && Value >= meterStartWh
            ? decimal.Round((Value - meterStartWh) / 1000m, 6)
            : null;

    public static decimal? StateOfCharge(IEnumerable<OcppMeterSample> samples, bool simulator)
    {
        foreach (
            var group in samples
                .Where(x =>
                    x.Measurand == "SoC"
                    && x.NumericValue is >= 0 and <= 100
                    && x.Unit is (null or "Percent")
                    && x.Format != "SignedData"
                )
                .OrderByDescending(x => x.ObservedAt)
                .ThenByDescending(x => x.ReceivedAt)
                .GroupBy(x => x.ObservedAt)
        )
        {
            var aggregate = group
                .Where(x => x.Phase == null)
                .Select(x => x.NumericValue)
                .Distinct()
                .ToArray();
            if (aggregate.Length > 0)
                return aggregate.Length == 1 ? aggregate[0] : null;
            // This simulator leaves the energy form's L1 default on SoC too.
            // Battery percentages are never added across phases. Restrict this
            // compatibility rule to demo devices and reject conflicting values.
            if (simulator)
            {
                var phased = group
                    .Where(x => x.Phase is "L1" or "L2" or "L3")
                    .Select(x => x.NumericValue)
                    .Distinct()
                    .ToArray();
                if (phased.Length > 0)
                    return phased.Length == 1 ? phased[0] : null;
            }
        }
        return null;
    }

    public static IReadOnlyList<OcppMeterReading> FromSamples(
        IEnumerable<OcppMeterSample> samples,
        DateTimeOffset startedAt,
        DateTimeOffset now,
        bool simulationClock = false
    )
    {
        var readings = new List<OcppMeterReading>();
        var groups = samples
            .Where(x =>
                x.NumericValue is >= 0 and < 1_000_000_000_000m
                && x.Format != "SignedData"
                && x.Location is null or "Outlet"
                && x.ObservedAt >= startedAt
                && x.ObservedAt <= (simulationClock ? now.AddDays(1) : now.AddMinutes(5))
                && x.Measurand is "Energy.Active.Import.Register" or "Power.Active.Import"
            )
            .GroupBy(x => new
            {
                x.MessageId,
                x.MeterValueIndex,
                x.ObservedAt,
                x.Measurand,
            });

        foreach (var group in groups)
        {
            var values = group
                .Select(x => new { Sample = x, Value = Normalize(x) })
                .Where(x => x.Value != null)
                .ToArray();
            var totals = values.Where(x => x.Sample.Phase == null).ToArray();
            decimal value;
            string scope;
            if (totals.Length == 1)
            {
                value = totals[0].Value!.Value;
                scope = "Total";
            }
            else if (totals.Length > 1)
                continue; // Ambiguous totals: do not choose an arbitrary reading.
            else
            {
                var phases = values
                    .Where(x => x.Sample.Phase is "L1" or "L2" or "L3")
                    .OrderBy(x => x.Sample.Phase)
                    .ToArray();
                if (
                    phases.Length == 0
                    || phases.Select(x => x.Sample.Phase).Distinct().Count() != phases.Length
                )
                    continue;
                value = phases.Sum(x => x.Value!.Value);
                scope =
                    phases.Length == 3
                        ? "Total"
                        : string.Join("+", phases.Select(x => x.Sample.Phase));
            }
            var limit =
                group.Key.Measurand == "Power.Active.Import" ? 999_999_999m : 1_000_000_000_000m;
            if (value >= limit)
                continue;
            readings.Add(
                new(
                    group.Key.Measurand,
                    value,
                    scope,
                    group.Key.ObservedAt,
                    group.Max(x => x.ReceivedAt)
                )
            );
        }
        return readings.OrderBy(x => x.ObservedAt).ThenBy(x => x.ReceivedAt).ToArray();
    }

    private static decimal? Normalize(OcppMeterSample sample) =>
        (sample.Measurand, sample.Unit) switch
        {
            ("Energy.Active.Import.Register", null or "Wh") => sample.NumericValue,
            ("Energy.Active.Import.Register", "kWh") => sample.NumericValue * 1000,
            ("Power.Active.Import", "W") => sample.NumericValue / 1000,
            ("Power.Active.Import", "kW") => sample.NumericValue,
            _ => null,
        };
}
