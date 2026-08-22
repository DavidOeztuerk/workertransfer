using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkerTransfer.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Loeschabsichten : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Guarded, the same way and for the same reason as the enum types
            // (Ü-7): the Python service already carries this table, and the
            // generated CreateTable would run into it. The columns are the ones
            // Alembic made, so both services read the same rows — a test pins
            // that against the real table.
            //
            // After the Python service is gone this is what creates the table on
            // a fresh database, which is why it is guarded rather than removed.
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS outbox (
                    id uuid NOT NULL,
                    user_id uuid NOT NULL,
                    kind character varying(64) NOT NULL,
                    created_at timestamp with time zone NOT NULL,
                    attempts integer NOT NULL,
                    delivered_at timestamp with time zone NULL,
                    last_error character varying(120) NOT NULL,
                    CONSTRAINT "PK_outbox" PRIMARY KEY (id));
                """);

            // Both are the dispatcher's search, on every tick.
            migrationBuilder.Sql(
                """CREATE INDEX IF NOT EXISTS "IX_outbox_created_at" ON outbox (created_at);""");
            migrationBuilder.Sql(
                """CREATE INDEX IF NOT EXISTS "IX_outbox_delivered_at" ON outbox (delivered_at);""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outbox");
        }
    }
}
