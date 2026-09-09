using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkerTransfer.Profile.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Räumt eine Aufzählung weg, die nie jemand benutzt hat.
    /// </summary>
    /// <remarks>
    /// <para><c>HasPostgresEnum&lt;T&gt;("pruef_handlung")</c> nimmt sein erstes
    /// Argument als <strong>Schema</strong>, nicht als Typnamen. Positionell
    /// geschrieben legte diese eine Zeile deshalb ein Schema
    /// <c>pruef_handlung</c> an und darin einen Typ <c>pruefhandlung</c> —
    /// während die erste Wanderung den echten Typ längst als
    /// <c>public.pruef_handlung</c> angelegt hatte.</para>
    ///
    /// <para>Gemessen an der laufenden Datenbank, alle elf durchgezählt: nur
    /// profile-service hatte das.</para>
    ///
    /// <code>
    ///  nspname        | typname
    ///  public         | pruef_handlung     &lt;- die benutzte
    ///  pruef_handlung | pruefhandlung      &lt;- die Waise
    /// </code>
    ///
    /// <para>Folgenlos im Betrieb — aber sie entstand bei JEDER Wanderung in
    /// JEDER Umgebung neu. Der Fehler sass im Modell und ist dort behoben;
    /// diese Wanderung räumt nur nach.</para>
    /// </remarks>
    public partial class WaisenSchemaEntfernen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:pruef_handlung", "profil_angelegt,profil_geaendert,profil_geloescht")
                .OldAnnotation("Npgsql:Enum:pruef_handlung", "profil_angelegt,profil_geaendert,profil_geloescht")
                .OldAnnotation("Npgsql:Enum:pruef_handlung.pruefhandlung", "profil_angelegt,profil_geaendert,profil_geloescht");

            // EF entfernt den Typ, das Schema bleibt leer stehen. Ein leeres
            // Schema mit dem Namen eines Aufzählungstyps ist genau die Spur,
            // über die der Nächste stolpert.
            //
            // RESTRICT und nicht CASCADE, mit Absicht: liegt dort wider Erwarten
            // noch etwas, soll die Wanderung ABBRECHEN statt es mitzunehmen. Ein
            // Aufräumschritt darf nie mehr können als aufräumen.
            migrationBuilder.Sql("DROP SCHEMA IF EXISTS pruef_handlung RESTRICT;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:pruef_handlung", "profil_angelegt,profil_geaendert,profil_geloescht")
                .Annotation("Npgsql:Enum:pruef_handlung.pruefhandlung", "profil_angelegt,profil_geaendert,profil_geloescht")
                .OldAnnotation("Npgsql:Enum:pruef_handlung", "profil_angelegt,profil_geaendert,profil_geloescht");
        }
    }
}
