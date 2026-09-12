using System.Reflection;
using FluentAssertions;
using WorkerTransfer.GitHub.Contracts;
using WorkerTransfer.GitHub.Domain.Verbindungen;

namespace WorkerTransfer.GitHub.Tests;

/// <summary>Die Grenze, an der genau hier gerutscht wird.</summary>
/// <remarks>
/// Dieser Dienst wurde <strong>unter</strong> ADR-0022 gebaut, nicht von ihm
/// verurteilt — das gelöschte Paket <c>worker-github</c> bewertete Menschen,
/// der Dienst tut ausdrücklich das Gegenteil. Damit das so bleibt, wird die
/// Grenze hier geprüft und nicht nur beschrieben:
/// <list type="bullet">
/// <item>Eine Aussage über ein <em>Repository</em> ist eine Tatsache — „laut
/// GitHub zu 80 % Go".</item>
/// <item>Eine Aussage über einen <em>Menschen</em> ist ein Urteil, das er nicht
/// kommentieren kann — „diese Person kann Go".</item>
/// </list>
/// Ein Sortierschlüssel über Menschen ist die ADR-0022-Punktzahl durch die
/// Hintertür, auch wenn er „Aktivität" heißt.
/// </remarks>
public class Adr0022Tests
{
    /// <summary>Worte, die ein Urteil über einen Menschen ankündigen.</summary>
    private static readonly string[] Verboten =
        ["score", "punkt", "rank", "rang", "level", "weight", "gewicht", "ability",
         "faehigkeit", "skill", "talent", "bewert", "aktivitaet", "activity"];

    /// <summary>
    /// Keine öffentliche Fläche dieses Dienstes trägt einen solchen Namen.
    /// </summary>
    /// <remarks>
    /// Der Test greift zwei Assemblies ab: die Domäne, wo ein Punktwert
    /// entstünde, und den Vertrag, wo er hinausginge. Wer eines der Worte
    /// braucht, hat die Grenze überschritten oder muss den Test ändern — und
    /// das ist ein sichtbarer Commit, den jemand begründen muss.
    /// </remarks>
    [Fact]
    public void Nichts_hier_fasst_einen_Menschen_in_einer_Zahl_zusammen()
    {
        var flaechen = new[] { typeof(Verbindung).Assembly, typeof(VerbindungV1).Assembly }
            .SelectMany(assembly => assembly.GetExportedTypes())
            .SelectMany(typ => typ.GetMembers(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.DeclaredOnly)
                .Select(glied => $"{typ.Name}.{glied.Name}")
                .Append(typ.Name))
            .Distinct()
            .ToList();

        flaechen.Should().NotBeEmpty("sonst prüft der Test nichts");

        foreach (var name in flaechen)
        {
            foreach (var wort in Verboten)
            {
                name.Contains(wort, StringComparison.OrdinalIgnoreCase)
                    .Should().BeFalse($"'{name}' trägt '{wort}' — ADR-0022");
            }
        }
    }

    /// <summary>
    /// Ein Repository trägt genau das, was GitHub gemeldet hat.
    /// </summary>
    /// <remarks>
    /// Acht Felder, alle abgeschrieben, keins gerechnet. Ein neuntes wäre die
    /// Stelle, an der aus einem Beleg eine Wertung wird.
    /// <para>
    /// <strong>Die zwei jüngsten sind der Grund, diesen Test genau zu lesen.</strong>
    /// <c>Themen</c> sind die Topics, die der Besitzer selbst gesetzt hat — eine
    /// Nennung, abgeschrieben. <c>Sprachen</c> ist die MENGE der Sprachen eines
    /// Repositories, und das ist die Grenze: GitHub liefert dazu ein Byte je
    /// Sprache, und genau daraus rechnete das gelöschte Paket sein „Können"
    /// (<c>bytes / total_bytes</c>, ADR-0022 §2). Die Namen sind eine Tatsache
    /// über ein Repository; die Zahlen wären das Rohmaterial für eine Aussage
    /// über einen Menschen. Sie stehen deshalb nirgends — siehe
    /// <c>HttpGitHubTests.Sprachen_kommen_als_Menge_ohne_Bytes</c>.
    /// </para>
    /// <para>
    /// Wer hier ein Feld ergänzt, prüft zuerst: kommt es SO von GitHub, oder
    /// ist es gerechnet? Nur das Erste darf dazu.
    /// </para>
    /// </remarks>
    [Fact]
    public void Ein_Repository_traegt_nur_Abgeschriebenes()
    {
        typeof(Repository).GetProperties()
            .Select(eigenschaft => eigenschaft.Name)
            .Should().BeEquivalentTo(
                nameof(Repository.Name),
                nameof(Repository.Beschreibung),
                nameof(Repository.Sprache),
                nameof(Repository.Sterne),
                nameof(Repository.Adresse),
                nameof(Repository.ZuletztGeschoben),
                nameof(Repository.Sprachen),
                nameof(Repository.Themen));
    }

    /// <summary>
    /// Und die Verbindung trägt keine Zusammenfassung über den Menschen.
    /// </summary>
    /// <remarks>
    /// <c>SprachenVollstaendig</c> ist die einzige Eigenschaft hier, die nicht
    /// von GitHub abgeschrieben ist — und sie darf es sein, weil sie über
    /// <em>uns</em> spricht und nicht über die Person: sie sagt, ob unser
    /// Abruf für jedes Repository die Sprachen holen konnte. Genau das
    /// verlangt ADR-0022 §3, und ohne sie läse sich eine gekürzte Menge wie
    /// eine vollständige.
    /// <para>
    /// Wer hier eine Eigenschaft ergänzt, beantwortet zuerst: spricht sie über
    /// den Abruf oder über den Menschen? Über den Menschen darf hier nichts
    /// stehen, das gerechnet ist.
    /// </para>
    /// </remarks>
    [Fact]
    public void Eine_Verbindung_traegt_keine_Zusammenfassung()
    {
        typeof(Verbindung).GetProperties()
            .Select(eigenschaft => eigenschaft.Name)
            .Should().BeEquivalentTo(
                nameof(Verbindung.Wer),
                nameof(Verbindung.Login),
                nameof(Verbindung.Einmalzeichenfolge),
                nameof(Verbindung.NachgewiesenAm),
                nameof(Verbindung.GeholtAm),
                nameof(Verbindung.Repositories),
                nameof(Verbindung.SprachenVollstaendig),
                nameof(Verbindung.Nachgewiesen));
    }
}
