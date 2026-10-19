using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkerTransfer.Transfer.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MarketWindow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "open_from",
                table: "market_status",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "open_until",
                table: "market_status",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "open_from",
                table: "market_status");

            migrationBuilder.DropColumn(
                name: "open_until",
                table: "market_status");
        }
    }
}
