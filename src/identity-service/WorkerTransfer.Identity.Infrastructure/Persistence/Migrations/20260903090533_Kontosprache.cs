using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkerTransfer.Identity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Kontosprache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // `"de"` und nicht `""`, obwohl `Sprachwahl.Aus` beides gleich
            // beantwortet: eine leere Zeichenkette in einer Sprachspalte ist
            // eine falsche Angabe, keine fehlende. Wer die Tabelle liest, soll
            // sehen, in welcher Kontosprache diese Konten geschrieben werden — und
            // das ist Deutsch, weil es die Kontosprache war, als sie entstanden.
            migrationBuilder.AddColumn<string>(
                name: "language",
                table: "users",
                type: "text",
                nullable: false,
                defaultValue: "de");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "language",
                table: "users");
        }
    }
}
