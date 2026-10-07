using EVCharging.Domain.Charging;
using EVCharging.Domain.Identity;
using EVCharging.Infrastructure.Persistence;

namespace EVCharging.Tests;

// These fixtures target DriverChargingConnections, before ChargingBusinessSessions.
// Never let today's EF mappings add columns that did not exist in that schema.
internal static class LegacyMigrationSeed
{
    public static FormattableString Account(Account account) =>
        $"""
            INSERT INTO accounts (id, email, normalized_email, password_hash, first_name, last_name,
                role, language, push_enabled, is_active, version, created_at)
            VALUES ({account.Id}, {account.Email}, {account.NormalizedEmail}, {account.PasswordHash},
                {account.FirstName}, {account.LastName}, {account.Role.ToString()}, {account.Language.ToString()},
                {account.PushEnabled}, {account.IsActive}, {account.Version}, {account.CreatedAt});
            """;

    public static FormattableString Station(ChargingStation station) =>
        $"""
            INSERT INTO charging_stations (id, code, name, city, address, latitude, longitude,
                connector_type, connector_count, power_kw, price_per_kwh, currency, status,
                is_archived, is_demo, version)
            VALUES ({station.Id}, {station.Code}, {station.Name}, {station.City}, {station.Address},
                {station.Latitude}, {station.Longitude}, {station.ConnectorType}, {station.ConnectorCount},
                {station.PowerKW}, {station.PricePerKWh}, {station.Currency}, {station.Status.ToString()},
                {station.IsArchived}, {station.IsDemo}, {station.Version});
            """;

    public static FormattableString Connection(OcppConnection connection) =>
        $"""
            INSERT INTO ocpp_connections (id, charge_point_id, subprotocol, connected_at)
            VALUES ({connection.Id}, {connection.ChargePointId}, {connection.Subprotocol}, {connection.ConnectedAt});
            """;

    public static FormattableString Message(OcppInboundMessage message) =>
        $"""
            INSERT INTO ocpp_inbound_messages (id, charge_point_id, connection_id, call_id, action,
                frame_json, payload_hash, processing_state, received_at)
            VALUES ({message.Id}, {message.ChargePointId}, {message.ConnectionId}, {message.CallId},
                {message.Action}, CAST({message.FrameJson} AS jsonb), {message.PayloadHash},
                {message.ProcessingState}, {message.ReceivedAt});
            """;

    public static FormattableString Claim(ChargingConnectionRequest claim) =>
        $"""
            INSERT INTO charging_connection_requests (id, driver_id, station_id, connector_number,
                state, created_at, expires_at, linked_at, session_id)
            VALUES ({claim.Id}, {claim.DriverId}, {claim.StationId}, {claim.ConnectorNumber},
                {claim.State}, {claim.CreatedAt}, {claim.ExpiresAt}, {claim.LinkedAt}, {claim.SessionId});
            """;

    public static FormattableString History(ChargingHistory history) =>
        $"""
            INSERT INTO charging_history (id, driver_id, station_id, started_at, ended_at,
                energy_kwh, unit_price_snapshot, total_cost, currency, is_demo)
            VALUES ({history.Id}, {history.DriverId}, {history.StationId}, {history.StartedAt}, {history.EndedAt},
                {history.EnergyKWh}, {history.UnitPriceSnapshot}, {history.TotalCost}, {history.Currency}, {history.IsDemo});
            """;
}
