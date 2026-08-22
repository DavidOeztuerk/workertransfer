using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkerTransfer.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HandlungsformDerSitzung : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The generated AlterDatabase() would CREATE TYPE unconditionally, and
            // Alembic already created this one — the same reason the tables it
            // owns carry ExcludeFromMigrations. Declared in the model all the
            // same, because without it EF sends the column as an integer.
            // Written as a guard rather than removed: once the Python service is
            // gone, this is what creates the type on a fresh database.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'audit_action') THEN
                        CREATE TYPE audit_action AS ENUM (
                            'register', 'login_success', 'login_failure',
                            'token_refresh', 'token_revoke',
                            'tenant_switch', 'tenant_switch_denied',
                            'email_verified', 'company_created',
                            'member_invited', 'member_joined',
                            'invitation_withdrawn', 'member_removed');
                    END IF;
                END $$;
                """);

            migrationBuilder.CreateTable(
                name: "session_capacities",
                columns: table => new
                {
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_session_capacities", x => x.session_id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "session_capacities");

            // The type stays. audit_events is not ours to unmake, and dropping
            // the type its column is declared as would take the table with it.
        }
    }
}
