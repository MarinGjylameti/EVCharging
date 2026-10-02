using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EVCharging.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChargingMeasurementClocks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "energy_observed_at",
                table: "charging_sessions",
                type: "timestamp with time zone",
                nullable: true);
            // Keep the published session migration unchanged. Existing installations
            // gain an independent energy clock without recreating any physical session.
            migrationBuilder.Sql("""
                UPDATE charging_sessions s SET energy_observed_at=COALESCE(
                  (SELECT max(m.observed_at) FROM ocpp_meter_samples m
                    JOIN ocpp_charging_transactions t ON t.id=m.transaction_id
                    WHERE t.session_id=s.id AND m.measurand='Energy.Active.Import.Register'
                      AND m.format IS DISTINCT FROM 'SignedData' AND m.numeric_value >= 0
                      AND (m.unit IS NULL OR m.unit IN ('Wh','kWh'))
                      AND (m.location IS NULL OR m.location='Outlet')
                      AND m.observed_at >= s.started_at AND m.observed_at <= s.last_measurement_at),
                  s.last_measurement_at, s.started_at)
                WHERE s.energy_kwh IS NOT NULL;
                UPDATE charging_stations SET status_source='Device', version=version+1 WHERE status_source='OCPP';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "energy_observed_at",
                table: "charging_sessions");
        }
    }
}
