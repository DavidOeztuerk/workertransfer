using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkerTransfer.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Zivilidentitaet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "family_name",
                table: "users",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "given_name",
                table: "users",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "addresses",
                columns: table => new
                {
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    line1 = table.Column<string>(type: "text", nullable: false),
                    line2 = table.Column<string>(type: "text", nullable: false),
                    postal_code = table.Column<string>(type: "text", nullable: false),
                    city = table.Column<string>(type: "text", nullable: false),
                    country = table.Column<string>(type: "text", nullable: false),
                    phone = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_addresses", x => x.subject_id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "addresses");

            migrationBuilder.DropColumn(
                name: "family_name",
                table: "users");

            migrationBuilder.DropColumn(
                name: "given_name",
                table: "users");
        }
    }
}
