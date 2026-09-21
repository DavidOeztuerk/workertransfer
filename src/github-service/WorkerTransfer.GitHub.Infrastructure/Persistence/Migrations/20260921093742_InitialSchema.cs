using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkerTransfer.GitHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "github_connections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    login = table.Column<string>(type: "character varying(39)", maxLength: 39, nullable: true),
                    challenge = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    verified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    fetched_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    repositories = table.Column<string>(type: "jsonb", nullable: false),
                    languages_complete = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_github_connections", x => x.id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "github_connections");
        }
    }
}
