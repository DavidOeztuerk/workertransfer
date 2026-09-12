using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkerTransfer.Resume.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UnterlagenUndVorlage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:audit_action", "resume_saved,resume_requested,request_granted,request_declined,access_revoked,subject_erased")
                .Annotation("Npgsql:Enum:document_kind", "zeugnis,zertifikat,sonstiges")
                .Annotation("Npgsql:Enum:request_status", "pending,granted,declined")
                .Annotation("Npgsql:Enum:resume_template", "schlicht,klassisch,modern")
                .OldAnnotation("Npgsql:Enum:audit_action", "resume_saved,resume_requested,request_granted,request_declined,access_revoked,subject_erased")
                .OldAnnotation("Npgsql:Enum:request_status", "pending,granted,declined");

            migrationBuilder.AddColumn<int>(
                name: "template",
                table: "resumes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

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

            migrationBuilder.CreateIndex(
                name: "IX_resume_documents_subject_id",
                table: "resume_documents",
                column: "subject_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "resume_documents");

            migrationBuilder.DropColumn(
                name: "template",
                table: "resumes");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:audit_action", "resume_saved,resume_requested,request_granted,request_declined,access_revoked,subject_erased")
                .Annotation("Npgsql:Enum:request_status", "pending,granted,declined")
                .OldAnnotation("Npgsql:Enum:audit_action", "resume_saved,resume_requested,request_granted,request_declined,access_revoked,subject_erased")
                .OldAnnotation("Npgsql:Enum:document_kind", "zeugnis,zertifikat,sonstiges")
                .OldAnnotation("Npgsql:Enum:request_status", "pending,granted,declined")
                .OldAnnotation("Npgsql:Enum:resume_template", "schlicht,klassisch,modern");
        }
    }
}
