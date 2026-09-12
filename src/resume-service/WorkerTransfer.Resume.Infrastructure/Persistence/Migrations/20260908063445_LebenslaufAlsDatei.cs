using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkerTransfer.Resume.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LebenslaufAlsDatei : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Die Spalte `kind` ist integer; der PG-Enum ist Annotation.
            // ADD VALUE statt DROP/CREATE, sonst faellt die Wanderung.
            migrationBuilder.Sql("""
                DO $migration$
                BEGIN
                    ALTER TYPE document_kind ADD VALUE IF NOT EXISTS 'lebenslauf';
                EXCEPTION
                    WHEN undefined_object THEN
                        CREATE TYPE document_kind AS ENUM ('zeugnis', 'zertifikat', 'sonstiges', 'lebenslauf');
                END
                $migration$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:audit_action", "resume_saved,resume_requested,request_granted,request_declined,access_revoked,subject_erased")
                .Annotation("Npgsql:Enum:document_kind", "zeugnis,zertifikat,sonstiges")
                .Annotation("Npgsql:Enum:request_status", "pending,granted,declined")
                .Annotation("Npgsql:Enum:resume_template", "schlicht,klassisch,modern")
                .OldAnnotation("Npgsql:Enum:audit_action", "resume_saved,resume_requested,request_granted,request_declined,access_revoked,subject_erased")
                .OldAnnotation("Npgsql:Enum:document_kind", "zeugnis,zertifikat,sonstiges,lebenslauf")
                .OldAnnotation("Npgsql:Enum:request_status", "pending,granted,declined")
                .OldAnnotation("Npgsql:Enum:resume_template", "schlicht,klassisch,modern");
        }
    }
}
