using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkerTransfer.Applications.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Bewerbungsentwuerfe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "application_draft_comments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    draft_id = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    quote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    resolved = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_application_draft_comments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "application_drafts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "text", nullable: false),
                    shares_resume = table.Column<bool>(type: "boolean", nullable: false),
                    documents = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_application_drafts", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_application_draft_comments_draft_id",
                table: "application_draft_comments",
                column: "draft_id");

            migrationBuilder.CreateIndex(
                name: "IX_application_drafts_subject_id",
                table: "application_drafts",
                column: "subject_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "application_draft_comments");

            migrationBuilder.DropTable(
                name: "application_drafts");
        }
    }
}
