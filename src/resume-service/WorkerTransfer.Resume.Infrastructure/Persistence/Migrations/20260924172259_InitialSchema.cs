using System;
using Microsoft.EntityFrameworkCore.Migrations;
using WorkerTransfer.Resume.Domain.Audit;
using WorkerTransfer.Resume.Domain.Requests;

#nullable disable

namespace WorkerTransfer.Resume.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:audit_action", "resume_saved,resume_requested,request_granted,request_declined,access_revoked,subject_erased")
                .Annotation("Npgsql:Enum:document_kind", "reference,certificate,other,curriculum_vitae")
                .Annotation("Npgsql:Enum:request_status", "pending,granted,declined")
                .Annotation("Npgsql:Enum:resume_template", "plain,classic,modern");

            migrationBuilder.CreateTable(
                name: "audit_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<AuditAction>(type: "audit_action", nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_events", x => x.id);
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

            migrationBuilder.CreateTable(
                name: "resume_document_terms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    has_text = table.Column<bool>(type: "boolean", nullable: false),
                    terms = table.Column<string>(type: "jsonb", nullable: false),
                    read_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_resume_document_terms", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "resume_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    content_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    size_bytes = table.Column<int>(type: "integer", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_resume_documents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "resume_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<RequestState>(type: "request_status", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    answered_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_resume_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "resumes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    positions = table.Column<string>(type: "jsonb", nullable: false),
                    education = table.Column<string>(type: "jsonb", nullable: false),
                    template = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_resumes", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_actor_id",
                table: "audit_events",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_target_id",
                table: "audit_events",
                column: "target_id");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_created_at",
                table: "outbox",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_outbox_delivered_at",
                table: "outbox",
                column: "delivered_at");

            migrationBuilder.CreateIndex(
                name: "IX_resume_document_terms_subject_id",
                table: "resume_document_terms",
                column: "subject_id");

            migrationBuilder.CreateIndex(
                name: "uq_terms_subject_document",
                table: "resume_document_terms",
                columns: new[] { "subject_id", "document_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_resume_documents_subject_id",
                table: "resume_documents",
                column: "subject_id");

            migrationBuilder.CreateIndex(
                name: "IX_resume_requests_subject_id",
                table: "resume_requests",
                column: "subject_id");

            migrationBuilder.CreateIndex(
                name: "IX_resume_requests_tenant_id",
                table: "resume_requests",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "uq_request_subject_tenant",
                table: "resume_requests",
                columns: new[] { "subject_id", "tenant_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_events");

            migrationBuilder.DropTable(
                name: "outbox");

            migrationBuilder.DropTable(
                name: "resume_document_terms");

            migrationBuilder.DropTable(
                name: "resume_documents");

            migrationBuilder.DropTable(
                name: "resume_requests");

            migrationBuilder.DropTable(
                name: "resumes");
        }
    }
}
