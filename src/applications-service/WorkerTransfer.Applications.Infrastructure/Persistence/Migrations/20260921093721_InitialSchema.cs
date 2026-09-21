using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkerTransfer.Applications.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
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
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    writing_started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_application_drafts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "applications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    shares_resume = table.Column<bool>(type: "boolean", nullable: false),
                    shares_portfolio = table.Column<bool>(type: "boolean", nullable: false),
                    documents = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    answered_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    applicant_contact = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_applications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    delivered_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_application_draft_comments_draft_id",
                table: "application_draft_comments",
                column: "draft_id");

            migrationBuilder.CreateIndex(
                name: "IX_application_drafts_subject_id",
                table: "application_drafts",
                column: "subject_id");

            migrationBuilder.CreateIndex(
                name: "IX_applications_job_id",
                table: "applications",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "IX_applications_subject_id",
                table: "applications",
                column: "subject_id");

            migrationBuilder.CreateIndex(
                name: "IX_applications_tenant_id",
                table: "applications",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "uq_application_job_subject",
                table: "applications",
                columns: new[] { "job_id", "subject_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbox_created_at",
                table: "outbox",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_delivered_at",
                table: "outbox",
                column: "delivered_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "application_draft_comments");

            migrationBuilder.DropTable(
                name: "application_drafts");

            migrationBuilder.DropTable(
                name: "applications");

            migrationBuilder.DropTable(
                name: "outbox");
        }
    }
}
