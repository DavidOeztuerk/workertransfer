using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkerTransfer.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NoeliaSitzungstabelle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_girder_refresh_tokens",
                table: "girder_refresh_tokens");

            migrationBuilder.RenameTable(
                name: "girder_refresh_tokens",
                newName: "noelia_refresh_tokens");

            migrationBuilder.RenameIndex(
                name: "IX_girder_refresh_tokens_TokenHash",
                table: "noelia_refresh_tokens",
                newName: "IX_noelia_refresh_tokens_TokenHash");

            migrationBuilder.RenameIndex(
                name: "IX_girder_refresh_tokens_SubjectId_RevokedAt",
                table: "noelia_refresh_tokens",
                newName: "IX_noelia_refresh_tokens_SubjectId_RevokedAt");

            migrationBuilder.RenameIndex(
                name: "IX_girder_refresh_tokens_SessionId",
                table: "noelia_refresh_tokens",
                newName: "IX_noelia_refresh_tokens_SessionId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_noelia_refresh_tokens",
                table: "noelia_refresh_tokens",
                column: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_noelia_refresh_tokens",
                table: "noelia_refresh_tokens");

            migrationBuilder.RenameTable(
                name: "noelia_refresh_tokens",
                newName: "girder_refresh_tokens");

            migrationBuilder.RenameIndex(
                name: "IX_noelia_refresh_tokens_TokenHash",
                table: "girder_refresh_tokens",
                newName: "IX_girder_refresh_tokens_TokenHash");

            migrationBuilder.RenameIndex(
                name: "IX_noelia_refresh_tokens_SubjectId_RevokedAt",
                table: "girder_refresh_tokens",
                newName: "IX_girder_refresh_tokens_SubjectId_RevokedAt");

            migrationBuilder.RenameIndex(
                name: "IX_noelia_refresh_tokens_SessionId",
                table: "girder_refresh_tokens",
                newName: "IX_girder_refresh_tokens_SessionId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_girder_refresh_tokens",
                table: "girder_refresh_tokens",
                column: "Id");
        }
    }
}
