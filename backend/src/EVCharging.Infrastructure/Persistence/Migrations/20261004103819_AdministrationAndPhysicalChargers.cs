using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EVCharging.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AdministrationAndPhysicalChargers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "credential_version",
                table: "ocpp_connections",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "authorized_tag_hashes_json",
                table: "ocpp_charge_points",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "credential_version",
                table: "ocpp_charge_points",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "management_version",
                table: "ocpp_charge_points",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "password_hash",
                table: "ocpp_charge_points",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "provisioned_serial_number",
                table: "ocpp_charge_points",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "location_id",
                table: "charging_stations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "charging_locations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    city = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    address = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: false),
                    longitude = table.Column<double>(type: "double precision", nullable: false),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_charging_locations", x => x.id);
                    table.CheckConstraint("ck_location_coordinates", "latitude BETWEEN -90 AND 90 AND longitude BETWEEN -180 AND 180");
                });

            migrationBuilder.CreateTable(
                name: "price_revisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    station_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_price = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: true),
                    price_per_kwh = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_price_revisions", x => x.id);
                    table.CheckConstraint("ck_revision_price", "price_per_kwh >= 0 AND (previous_price IS NULL OR previous_price >= 0)");
                    table.ForeignKey(
                        name: "FK_price_revisions_accounts_actor_id",
                        column: x => x.actor_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_price_revisions_charging_stations_station_id",
                        column: x => x.station_id,
                        principalTable: "charging_stations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "administrator_grants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    administrator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    station_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_administrator_grants", x => x.id);
                    table.CheckConstraint("ck_grant_one_resource", "(location_id IS NULL) <> (station_id IS NULL)");
                    table.ForeignKey(
                        name: "FK_administrator_grants_accounts_administrator_id",
                        column: x => x.administrator_id,
                        principalTable: "accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_administrator_grants_charging_locations_location_id",
                        column: x => x.location_id,
                        principalTable: "charging_locations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_administrator_grants_charging_stations_station_id",
                        column: x => x.station_id,
                        principalTable: "charging_stations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_charging_stations_location_id",
                table: "charging_stations",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "IX_administrator_grants_administrator_id_location_id",
                table: "administrator_grants",
                columns: new[] { "administrator_id", "location_id" },
                unique: true,
                filter: "location_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_administrator_grants_administrator_id_station_id",
                table: "administrator_grants",
                columns: new[] { "administrator_id", "station_id" },
                unique: true,
                filter: "station_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_administrator_grants_location_id",
                table: "administrator_grants",
                column: "location_id");

            migrationBuilder.CreateIndex(
                name: "IX_administrator_grants_station_id",
                table: "administrator_grants",
                column: "station_id");

            migrationBuilder.CreateIndex(
                name: "IX_charging_locations_is_archived_name",
                table: "charging_locations",
                columns: new[] { "is_archived", "name" });

            migrationBuilder.CreateIndex(
                name: "IX_price_revisions_actor_id",
                table: "price_revisions",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "IX_price_revisions_station_id_recorded_at",
                table: "price_revisions",
                columns: new[] { "station_id", "recorded_at" });

            migrationBuilder.AddForeignKey(
                name: "FK_charging_stations_charging_locations_location_id",
                table: "charging_stations",
                column: "location_id",
                principalTable: "charging_locations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_charging_stations_charging_locations_location_id",
                table: "charging_stations");

            migrationBuilder.DropTable(
                name: "administrator_grants");

            migrationBuilder.DropTable(
                name: "price_revisions");

            migrationBuilder.DropTable(
                name: "charging_locations");

            migrationBuilder.DropIndex(
                name: "IX_charging_stations_location_id",
                table: "charging_stations");

            migrationBuilder.DropColumn(
                name: "credential_version",
                table: "ocpp_connections");

            migrationBuilder.DropColumn(
                name: "authorized_tag_hashes_json",
                table: "ocpp_charge_points");

            migrationBuilder.DropColumn(
                name: "credential_version",
                table: "ocpp_charge_points");

            migrationBuilder.DropColumn(
                name: "management_version",
                table: "ocpp_charge_points");

            migrationBuilder.DropColumn(
                name: "password_hash",
                table: "ocpp_charge_points");

            migrationBuilder.DropColumn(
                name: "provisioned_serial_number",
                table: "ocpp_charge_points");

            migrationBuilder.DropColumn(
                name: "location_id",
                table: "charging_stations");
        }
    }
}
