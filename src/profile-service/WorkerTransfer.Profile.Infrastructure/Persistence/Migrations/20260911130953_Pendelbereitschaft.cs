using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkerTransfer.Profile.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Pendelbereitschaft : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "commute_km",
                table: "profiles",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "relocation",
                table: "profiles",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "commute_km",
                table: "profiles");

            migrationBuilder.DropColumn(
                name: "relocation",
                table: "profiles");
        }
    }
}
