using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EVCharging.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PersistentApplicationLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "application_logs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    instance_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    identity = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    trace_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    connection_id = table.Column<Guid>(type: "uuid", nullable: true),
                    context_json = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_application_logs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_application_logs_connection_id_id",
                table: "application_logs",
                columns: new[] { "connection_id", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_application_logs_event_id",
                table: "application_logs",
                column: "event_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_application_logs_identity_id",
                table: "application_logs",
                columns: new[] { "identity", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_application_logs_level_id",
                table: "application_logs",
                columns: new[] { "level", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_application_logs_recorded_at",
                table: "application_logs",
                column: "recorded_at");

            migrationBuilder.CreateIndex(
                name: "IX_application_logs_source_id",
                table: "application_logs",
                columns: new[] { "source", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_application_logs_trace_id_id",
                table: "application_logs",
                columns: new[] { "trace_id", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "application_logs");
        }
    }
}
