using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EVCharging.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DriverMapAndHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "charging_stations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    city = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    address = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: false),
                    longitude = table.Column<double>(type: "double precision", nullable: false),
                    connector_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    connector_count = table.Column<int>(type: "integer", nullable: false),
                    power_kw = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    price_per_kwh = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false),
                    is_demo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_charging_stations", x => x.id);
                    table.CheckConstraint("ck_station_capacity", "connector_count > 0 AND power_kw > 0 AND price_per_kwh >= 0");
                    table.CheckConstraint("ck_station_coordinates", "latitude BETWEEN -90 AND 90 AND longitude BETWEEN -180 AND 180");
                });

            migrationBuilder.CreateTable(
                name: "charging_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    driver_id = table.Column<Guid>(type: "uuid", nullable: false),
                    station_id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    energy_kwh = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    unit_price_snapshot = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    total_cost = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    is_demo = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_charging_history", x => x.id);
                    table.CheckConstraint("ck_history_measurement", "energy_kwh >= 0 AND unit_price_snapshot >= 0 AND total_cost >= 0 AND ended_at >= started_at");
                    table.ForeignKey(
                        name: "FK_charging_history_accounts_driver_id",
                        column: x => x.driver_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_charging_history_charging_stations_station_id",
                        column: x => x.station_id,
                        principalTable: "charging_stations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_charging_history_driver_id_started_at",
                table: "charging_history",
                columns: new[] { "driver_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "IX_charging_history_station_id",
                table: "charging_history",
                column: "station_id");

            migrationBuilder.CreateIndex(
                name: "IX_charging_stations_city_is_archived",
                table: "charging_stations",
                columns: new[] { "city", "is_archived" });

            migrationBuilder.CreateIndex(
                name: "IX_charging_stations_code",
                table: "charging_stations",
                column: "code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "charging_history");

            migrationBuilder.DropTable(
                name: "charging_stations");
        }
    }
}
