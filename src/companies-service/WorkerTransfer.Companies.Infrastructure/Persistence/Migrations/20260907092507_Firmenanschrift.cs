using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkerTransfer.Companies.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Firmenanschrift : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "city",
                table: "company_profiles",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "country",
                table: "company_profiles",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "line1",
                table: "company_profiles",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "phone",
                table: "company_profiles",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "postal_code",
                table: "company_profiles",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "city",
                table: "company_profiles");

            migrationBuilder.DropColumn(
                name: "country",
                table: "company_profiles");

            migrationBuilder.DropColumn(
                name: "line1",
                table: "company_profiles");

            migrationBuilder.DropColumn(
                name: "phone",
                table: "company_profiles");

            migrationBuilder.DropColumn(
                name: "postal_code",
                table: "company_profiles");
        }
    }
}
