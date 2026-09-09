using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkerTransfer.Transfer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Transfermarkt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "market_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    answered_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_market_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "market_status",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    availability = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    employed = table.Column<bool>(type: "boolean", nullable: false),
                    note = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_market_status", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    delivered_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "transfers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    requires_release = table.Column<bool>(type: "boolean", nullable: false),
                    release_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    offer_note = table.Column<string>(type: "text", nullable: false),
                    offer_start_on = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    offer_fee_cents = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transfers", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_market_requests_subject_id",
                table: "market_requests",
                column: "subject_id");

            migrationBuilder.CreateIndex(
                name: "IX_market_requests_tenant_id",
                table: "market_requests",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "uq_market_requests_subject_tenant",
                table: "market_requests",
                columns: new[] { "subject_id", "tenant_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbox_created_at",
                table: "outbox",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_delivered_at",
                table: "outbox",
                column: "delivered_at");

            migrationBuilder.CreateIndex(
                name: "IX_transfers_subject_id",
                table: "transfers",
                column: "subject_id");

            migrationBuilder.CreateIndex(
                name: "IX_transfers_tenant_id",
                table: "transfers",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "uq_running_transfer",
                table: "transfers",
                columns: new[] { "subject_id", "tenant_id" },
                unique: true,
                filter: "status IN ('interested', 'talking', 'offered', 'accepted')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "market_requests");

            migrationBuilder.DropTable(
                name: "market_status");

            migrationBuilder.DropTable(
                name: "outbox");

            migrationBuilder.DropTable(
                name: "transfers");
        }
    }
}
