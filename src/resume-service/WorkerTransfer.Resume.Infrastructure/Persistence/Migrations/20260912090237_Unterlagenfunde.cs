using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkerTransfer.Resume.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Unterlagenfunde : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.CreateIndex(
                name: "IX_resume_document_terms_subject_id",
                table: "resume_document_terms",
                column: "subject_id");

            migrationBuilder.CreateIndex(
                name: "uq_terms_subject_document",
                table: "resume_document_terms",
                columns: new[] { "subject_id", "document_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "resume_document_terms");
        }
    }
}
