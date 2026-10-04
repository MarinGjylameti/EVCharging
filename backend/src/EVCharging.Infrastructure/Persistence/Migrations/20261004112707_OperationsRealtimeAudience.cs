using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EVCharging.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OperationsRealtimeAudience : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_realtime_outbox_audience",
                table: "realtime_outbox");

            migrationBuilder.AddCheckConstraint(
                name: "ck_realtime_outbox_audience",
                table: "realtime_outbox",
                sql: "audience IN ('PublicStation', 'Driver', 'Admin', 'Operations') AND (audience <> 'Driver' OR recipient_account_id IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_realtime_outbox_audience",
                table: "realtime_outbox");

            migrationBuilder.AddCheckConstraint(
                name: "ck_realtime_outbox_audience",
                table: "realtime_outbox",
                sql: "audience IN ('PublicStation', 'Driver', 'Admin') AND (audience <> 'Driver' OR recipient_account_id IS NOT NULL)");
        }
    }
}
