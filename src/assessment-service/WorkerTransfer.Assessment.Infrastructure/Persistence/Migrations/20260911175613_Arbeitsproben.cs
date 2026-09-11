using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkerTransfer.Assessment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Arbeitsproben : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "assessments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    task = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    hours = table.Column<int>(type: "integer", nullable: false),
                    due_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    submission_text = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    submission_url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    submitted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    evaluation_text = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    evaluation_outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    evaluated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_assessments", x => x.id);
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
                name: "IX_assessments_subject_id",
                table: "assessments",
                column: "subject_id");

            migrationBuilder.CreateIndex(
                name: "IX_assessments_tenant_id",
                table: "assessments",
                column: "tenant_id");

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
                name: "assessments");

            migrationBuilder.DropTable(
                name: "outbox");
        }
    }
}
