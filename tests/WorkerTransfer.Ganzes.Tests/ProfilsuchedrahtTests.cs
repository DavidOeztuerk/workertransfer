using System.Text.Json;
using FluentAssertions;
using WorkerTransfer.Profile.Contracts;
using WorkerTransfer.Scout.Contracts;

namespace WorkerTransfer.Ganzes.Tests;

/// <summary>
/// Was profile-service schreibt, muss scout-service lesen können.
/// </summary>
/// <remarks>
/// <para><strong>Warum dieser Test und nicht ein Namensvergleich.</strong> Ein
/// Test, der fragt „heisst das Feld <c>subject_id</c>?", prüft dieselbe
/// Zeichenkette zweimal und geht mit ihr gemeinsam kaputt. Hier wird stattdessen
/// der Rumpf serialisiert, den der Absender wirklich schreibt, und mit dem
/// <em>Typ des Empfängers</em> gelesen. Grün heisst dann: die Werte kommen an.
/// Das ist die Zusage, und sie überlebt jede Umbenennung.</para>
///
/// <para><strong>Der Anlass ist älter als dieser Dienst.</strong> Vier Dienste
/// schickten einmal <c>userId</c> an einen Empfänger, der
/// <c>[JsonPropertyName("user_id")]</c> deklariert: die Kennung band still auf
/// <c>Guid.Empty</c>, und der ganze Benachrichtigungsweg hat nie funktioniert —
/// 18 Ausgangszeilen, keine einzige Mail. Bei einwortigen Feldern
/// (<c>headline</c>, <c>location</c>) fällt so etwas nie auf; erst ein
/// zusammengesetzter Name geht auseinander, und <c>subject_id</c> und
/// <c>remote_ok</c> sind genau solche.</para>
///
/// <para><strong>Warum keine andere Reihe es fängt.</strong>
/// <c>DrahtvertragTests</c> prüft, dass beide Seiten <em>eine</em> Angabe tragen
/// — nicht, dass es dieselbe ist. Und keine Dienstreihe kreuzt die Naht: der
/// Absender hat im Test keinen Empfänger, der Empfänger keinen Absender.</para>
/// </remarks>
public class ProfilsuchedrahtTests
{
    private static readonly Guid Wer = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301");

    /// <summary>Ein Profil überlebt die Naht — mit Kennung und Remote-Häkchen.</summary>
    [Fact]
    public void Ein_Profil_kommt_vollstaendig_an()
    {
        var geschrieben = JsonSerializer.Serialize(
            new ProfilfundV1(Wer, "Entwicklerin", "Berlin", true, ["Go", "PostgreSQL"]));

        var gelesen = JsonSerializer.Deserialize<FremdprofilV1>(geschrieben);

        gelesen.Should().NotBeNull();
        gelesen!.SubjectId.Should().Be(Wer, "ohne die Kennung ist ein Treffer niemand");
        gelesen.Headline.Should().Be("Entwicklerin");
        gelesen.Location.Should().Be("Berlin");
        gelesen.RemoteOk.Should().BeTrue(
            "ein bool hat keine Not-Null-Sperre: er fiele still auf false zurück");
        gelesen.Skills.Should().Equal("Go", "PostgreSQL");
    }

    /// <summary>Und eine Seite samt ihrem Zeiger.</summary>
    /// <remarks>
    /// Ohne den Zeiger gäbe es keine zweite Seite — und das sähe aus wie „es
    /// gibt niemanden mehr".
    /// </remarks>
    [Fact]
    public void Eine_Seite_kommt_mit_ihrem_Zeiger_an()
    {
        var geschrieben = JsonSerializer.Serialize(
            new ProfilfundseiteV1(
                [new ProfilfundV1(Wer, "Entwicklerin", "Berlin", false, ["Go"])],
                "ZWVpbg=="));

        var gelesen = JsonSerializer.Deserialize<FremdprofilseiteV1>(geschrieben);

        gelesen.Should().NotBeNull();
        gelesen!.Items.Should().ContainSingle().Which.SubjectId.Should().Be(Wer);
        gelesen.Next.Should().Be("ZWVpbg==");
    }

    /// <summary>
    /// Die Gegenprobe: der Test würde ein falsch benanntes Feld auch bemerken.
    /// </summary>
    /// <remarks>
    /// Eine Reihe, die nichts finden kann, ist grün und wertlos. Hier wird
    /// derselbe Rumpf mit dem camelCase-Namen geschrieben, den ein vergessenes
    /// <c>[JsonPropertyName]</c> erzeugen würde — und der Empfänger liest dann
    /// eine leere Kennung.
    /// </remarks>
    [Fact]
    public void Ein_falsch_benanntes_Feld_faellt_auf()
    {
        var camelCase = """
            {"subjectId":"3f2504e0-4f89-11d3-9a0c-0305e82c3301","headline":"x",
             "location":"y","remoteOk":true,"skills":[]}
            """;

        var gelesen = JsonSerializer.Deserialize<FremdprofilV1>(camelCase);

        gelesen!.SubjectId.Should().Be(
            Guid.Empty, "genau so band `userId` einmal still auf Guid.Empty");
        gelesen.RemoteOk.Should().BeFalse();
    }
}
