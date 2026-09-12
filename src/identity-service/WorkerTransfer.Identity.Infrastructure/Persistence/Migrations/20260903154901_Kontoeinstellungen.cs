using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkerTransfer.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Kontoeinstellungen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "account_settings",
                columns: table => new
                {
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delete_after_months = table.Column<int>(type: "integer", nullable: true),
                    ai_provider = table.Column<string>(type: "text", nullable: false),
                    ai_base_url = table.Column<string>(type: "text", nullable: false),
                    ai_model = table.Column<string>(type: "text", nullable: false),
                    ai_key_encrypted = table.Column<string>(type: "text", nullable: false),
                    ai_key_tail = table.Column<string>(type: "text", nullable: false),
                    ai_audit_log = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_settings", x => x.subject_id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_settings");
        }
    }
}
