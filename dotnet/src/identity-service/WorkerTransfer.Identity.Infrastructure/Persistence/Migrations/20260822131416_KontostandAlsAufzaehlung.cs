using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkerTransfer.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class KontostandAlsAufzaehlung : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Same guard as HandlungsformDerSitzung, and for the same reason:
            // Alembic already created this type, and the generated
            // AlterDatabase() would create it again. Declared in the model all
            // the same, because without it EF sends the column as an integer.
            // Ü-7 in docs/uebergang-python-dotnet.md says when it becomes a
            // plain CREATE TYPE again.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'account_status') THEN
                        CREATE TYPE account_status AS ENUM (
                            'pending', 'active', 'suspended', 'disabled');
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The type stays. users is not ours to unmake, and dropping the
            // type its column is declared as would take the table with it.
        }
    }
}
