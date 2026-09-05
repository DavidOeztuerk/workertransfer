using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkerTransfer.GitHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SprachenVollstaendig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "languages_complete",
                table: "github_connections",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "languages_complete",
                table: "github_connections");
        }
    }
}
