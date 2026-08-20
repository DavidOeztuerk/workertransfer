using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkerTransfer.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ErneuerungsTokenTabelle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:citext", ",,");

            migrationBuilder.CreateTable(
                name: "girder_refresh_tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SessionStartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SessionExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReplacedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ClientFingerprint = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_girder_refresh_tokens", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_girder_refresh_tokens_SessionId",
                table: "girder_refresh_tokens",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_girder_refresh_tokens_SubjectId_RevokedAt",
                table: "girder_refresh_tokens",
                columns: new[] { "SubjectId", "RevokedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_girder_refresh_tokens_TokenHash",
                table: "girder_refresh_tokens",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "girder_refresh_tokens");
        }
    }
}
