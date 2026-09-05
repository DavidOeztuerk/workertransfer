using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkerTransfer.GitHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class KontoDarfOffenBleiben : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "login",
                table: "github_connections",
                type: "character varying(39)",
                maxLength: 39,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(39)",
                oldMaxLength: 39);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "login",
                table: "github_connections",
                type: "character varying(39)",
                maxLength: 39,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(39)",
                oldMaxLength: 39,
                oldNullable: true);
        }
    }
}
