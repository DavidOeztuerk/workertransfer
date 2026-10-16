using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkerTransfer.Transfer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EmployerRouteAndRelease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "offer_fee_cents",
                table: "transfers");

            migrationBuilder.CreateTable(
                name: "employer_routes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opened_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employer_routes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "employer_settings",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    accepts_inquiries = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employer_settings", x => x.tenant_id);
                });

            migrationBuilder.CreateTable(
                name: "release_negotiations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    transfer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hiring_tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    current_tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    release_month = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    fee_cents = table.Column<long>(type: "bigint", nullable: true),
                    terms_by = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_release_negotiations", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_release_negotiations_current_tenant_id",
                table: "release_negotiations",
                column: "current_tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_release_negotiations_subject_id",
                table: "release_negotiations",
                column: "subject_id");

            migrationBuilder.CreateIndex(
                name: "uq_release_negotiations_transfer",
                table: "release_negotiations",
                column: "transfer_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "employer_routes");

            migrationBuilder.DropTable(
                name: "employer_settings");

            migrationBuilder.DropTable(
                name: "release_negotiations");

            migrationBuilder.AddColumn<long>(
                name: "offer_fee_cents",
                table: "transfers",
                type: "bigint",
                nullable: true);
        }
    }
}
