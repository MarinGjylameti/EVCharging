using System.Text.Json;
using EVCharging.Application.Common;
using EVCharging.Domain.Identity;
using EVCharging.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EVCharging.Infrastructure.Charging;

internal static class DeviceDriverAssignment
{
    public static string? ParseCard(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;
        if (input.Length > 4096)
            throw InvalidCard();
        var tag = input.Trim();
        if (tag.StartsWith('{') || tag.StartsWith('[') || tag.StartsWith('"'))
        {
            try
            {
                using var document = JsonDocument.Parse(
                    tag,
                    new JsonDocumentOptions { MaxDepth = 8 }
                );
                var payload = document.RootElement;
                if (payload.ValueKind == JsonValueKind.Array)
                {
                    if (
                        payload.GetArrayLength() != 4
                        || payload[0].ValueKind != JsonValueKind.Number
                        || !payload[0].TryGetInt32(out var messageType)
                        || messageType != 2
                        || payload[2].ValueKind != JsonValueKind.String
                        || payload[2].GetString() is not ("Authorize" or "StartTransaction")
                    )
                        throw InvalidCard();
                    payload = payload[3];
                }
                if (payload.ValueKind == JsonValueKind.Object)
                {
                    if (
                        payload.EnumerateObject().Count(p => p.Name == "idTag") != 1
                        || !payload.TryGetProperty("idTag", out var property)
                    )
                        throw InvalidCard();
                    payload = property;
                }
                if (payload.ValueKind != JsonValueKind.String)
                    throw InvalidCard();
                tag = payload.GetString()!;
            }
            catch (JsonException)
            {
                throw InvalidCard();
            }
        }
        if (string.IsNullOrWhiteSpace(tag) || tag.Length > 20 || tag.Any(char.IsControl))
            throw InvalidCard();
        return tag;
    }

    private static RequestFailure InvalidCard() =>
        new(
            400,
            "invalid_card_input",
            "Enter a card code (1–20 characters) or JSON containing one string idTag."
        );

    public static Task<OcppChargePoint?> ForDriverAsync(
        ChargingDbContext db,
        Guid driverId,
        Guid stationId,
        CancellationToken ct
    ) =>
        db
            .OcppChargePoints.AsNoTracking()
            .SingleOrDefaultAsync(
                d =>
                    d.AssignedDriverId == driverId
                    && d.IsEnabled
                    && d.ProvisionedSerialNumber != null
                    && db.Accounts.Any(a =>
                        a.Id == driverId
                        && a.Role == UserRole.Driver
                        && a.IsActive
                        && a.ArchivedAt == null
                    )
                    && db.OcppConnectors.Any(c =>
                        c.ChargePointId == d.Id && c.StationId == stationId
                    ),
                ct
            );
}
