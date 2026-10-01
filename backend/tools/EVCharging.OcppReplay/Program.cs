using System.Text.Json;
using EVCharging.Domain.Charging;
using EVCharging.Infrastructure.Charging;

// Offline fixture replay, not a WebSocket server. IDs, tariff and device below are synthetic.
var frames = File.ReadLines(Path.Combine(AppContext.BaseDirectory, "sample-session.jsonl"));
ChargingSession? session = null;
decimal startRegister = 0;
var accepted = 0;
var duplicates = 0;
foreach (var frame in frames.Where(x => !string.IsNullOrWhiteSpace(x)))
{
    foreach (
        var message in Ocpp16TelemetryParser.ParseCall(
            "SIMULATED-CP",
            frame,
            DateTimeOffset.Parse("2026-09-28T17:01:00Z")
        )
    )
    {
        if (message.Action == "StartTransaction")
        {
            if (session is not null)
                throw new InvalidOperationException("Fixture has multiple starts.");
            startRegister = message.MeterRegisterKWh!.Value;
            session = new(
                Guid.Parse("00000000-0000-0000-0000-000000000001"),
                0.35m,
                "EUR",
                message.ObservedAt
            );
            accepted++;
        }
        else
        {
            if (session is null || message.TransactionId != 1)
                throw new InvalidOperationException("Unmapped fixture transaction.");
            var energy = message.MeterRegisterKWh!.Value - startRegister;
            var changed =
                message.Action == "StopTransaction"
                    ? session.Complete(message.EventId, energy, message.ObservedAt)
                    : session.Measure(message.EventId, energy, message.PowerKW, message.ObservedAt);
            if (changed)
                accepted++;
            else
                duplicates++;
        }
    }
}
if (
    session?.EndedAt is null
    || session.EnergyKWh != 20m
    || session.CalculatedCost != 7m
    || duplicates != 1
)
    throw new InvalidOperationException("Replay verification failed.");
Console.WriteLine(
    JsonSerializer.Serialize(
        new
        {
            simulated = true,
            protocol = "OCPP 1.6J telemetry",
            energyKWh = session.EnergyKWh,
            samplePricePerKWh = session.UnitPriceSnapshot,
            calculatedCost = session.CalculatedCost,
            currency = session.Currency,
            acceptedEvents = accepted,
            ignoredDuplicates = duplicates,
            hardwareConnected = false,
            driverOwnershipVerified = false,
        },
        new JsonSerializerOptions { WriteIndented = true }
    )
);
