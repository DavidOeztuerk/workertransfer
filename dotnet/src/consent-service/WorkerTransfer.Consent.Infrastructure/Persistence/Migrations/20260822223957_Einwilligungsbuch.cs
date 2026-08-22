using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WorkerTransfer.Consent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Einwilligungsbuch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    metadata = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_events", x => x.id);
                    table.CheckConstraint("ck_audit_events_action", "action IN ('consent_grant','consent_revoke','consent_delete')");
                });

            migrationBuilder.CreateTable(
                name: "consent_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    capability = table.Column<string>(type: "text", nullable: false),
                    action = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    recorded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consent_events", x => x.id);
                    table.CheckConstraint("ck_consent_events_action", "action IN ('GRANT','REVOKE','DELETE')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_actor",
                table: "audit_events",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_target",
                table: "audit_events",
                column: "target_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_tenant",
                table: "audit_events",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_consent_events_lookup",
                table: "consent_events",
                columns: new[] { "subject_id", "capability", "recorded_at", "event_id" },
                descending: new[] { false, false, true, true });

            migrationBuilder.CreateIndex(
                name: "ux_consent_events_event",
                table: "consent_events",
                column: "event_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_events");

            migrationBuilder.DropTable(
                name: "consent_events");
        }
    }
}
