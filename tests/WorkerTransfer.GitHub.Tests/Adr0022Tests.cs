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
    /// Sechs Felder, alle abgeschrieben, keins gerechnet. Ein siebtes wäre die
    /// Stelle, an der aus einem Beleg eine Wertung wird.
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
                nameof(Repository.ZuletztGeschoben));
    }

    /// <summary>
    /// Und die Verbindung trägt keine Zusammenfassung über den Menschen.
    /// </summary>
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
                nameof(Verbindung.Nachgewiesen));
    }
}
