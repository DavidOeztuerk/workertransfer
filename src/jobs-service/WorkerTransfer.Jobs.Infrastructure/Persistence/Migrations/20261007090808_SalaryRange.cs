using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkerTransfer.Jobs.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SalaryRange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "salary_max",
                table: "jobs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "salary_min",
                table: "jobs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "salary_period",
                table: "jobs",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "salary_max",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "salary_min",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "salary_period",
                table: "jobs");
        }
    }
}
