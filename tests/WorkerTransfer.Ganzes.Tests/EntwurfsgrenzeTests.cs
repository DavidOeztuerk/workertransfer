using System.Reflection;
using FluentAssertions;
using WorkerTransfer.Jobs.Application.Ports;
using WorkerTransfer.Profile.Application.Ports;

namespace WorkerTransfer.Ganzes.Tests;

/// <summary>
/// Was an das Modell hinausgeht, ist eine <strong>geschlossene</strong> Menge
/// (ADR-0024 §3).
/// </summary>
/// <remarks>
/// <para>ADR-0024 sagt zu: „Eine Datenklasse und kein freies Wörterbuch, weil
/// ein Wörterbuch beim nächsten Merkmal stillschweigend einen Schlüssel mehr
/// trägt. <strong>Ein Test nagelt die Feldmenge fest.</strong>" Und CLAUDE.md
/// sagt dasselbe für beide Seiten: „Tests pin both field sets."</para>
///
/// <para><strong>Den Test gab es nicht.</strong> Was es gab, prüft die fertige
/// Prompt-<em>Zeichenkette</em> auf eine SubjectId und ein <c>@</c>
/// (<c>Der_Entwurf_traegt_nichts_ueber_die_Person_hinaus</c>). Das ist etwas
/// anderes und deutlich schwächer: ein neues Feld <c>Arbeitgeber</c> auf
/// <see cref="Entwurfslage"/>, gefüllt mit „Muster GmbH", enthält weder eine
/// Guid noch ein <c>@</c> und käme durch. Genau die stillschweigende Erweiterung,
/// gegen die der Record überhaupt gewählt wurde, wäre also ungeprüft geblieben.</para>
///
/// <para>Geprüft wird am <em>Typ</em> und gegen eine ausgeschriebene Menge, nicht
/// gegen eine Verbotsliste. Eine Verbotsliste beantwortet „ist dieser Name
/// heikel?" — eine Frage, die man beim nächsten Feld neu und womöglich falsch
/// beantwortet. Eine geschlossene Menge verlangt stattdessen, dass wer ein Feld
/// hinzufügt, es hier hinschreibt: dieselbe Bewegung, aber eine, die auffällt.</para>
///
/// <para><strong>Zwei Mengen, nicht eine.</strong> Die beiden Entwürfe sind
/// Spiegelbilder und dürfen nie zusammenwachsen: der Profilkontext hilft einer
/// Person, über sich zu schreiben, der Anzeigenkontext einem Unternehmen, über
/// die eigene Stelle. Ein gemeinsamer Typ wäre die Stelle, an der „erfinde nichts
/// über die Person" eines Tages für eine Anzeige gälte — oder schlimmer, umgekehrt.</para>
/// </remarks>
public class EntwurfsgrenzeTests
{
    /// <summary>
    /// Der Profilkontext trägt vier Felder — und die Person hat jedes davon
    /// selbst geschrieben.
    /// </summary>
    /// <remarks>
    /// Kein Name, keine E-Mail-Adresse, keine <c>SubjectId</c>, kein Arbeitgeber,
    /// kein Lebenslauf, keine Bewerbung, kein Marktstatus. <c>Wunsch</c> ist die
    /// einzige Zeile, die der Aufrufer in diesem Moment beisteuert.
    /// </remarks>
    [Fact]
    public void Der_Profilkontext_traegt_genau_vier_Felder()
    {
        Felder(typeof(Entwurfslage)).Should().BeEquivalentTo(
            ["Ueberschrift", "Text", "Faehigkeiten", "Wunsch", "Prompt"],
            "ADR-0024 §3 nennt diese Menge, und wer sie erweitert, "
            + "schickt etwas Neues über eine Person hinaus");
    }

    /// <summary>
    /// Der Anzeigenkontext trägt fünf — und keines davon sagt, WER ausschreibt.
    /// </summary>
    /// <remarks>
    /// Kein Mandant und kein Firmenname: die Anzeige soll formuliert werden,
    /// nicht das Unternehmen beschrieben. Ein Firmenname im Prompt wäre die
    /// Einladung, das Modell etwas über den Arbeitgeber sagen zu lassen, was
    /// niemand geprüft hat.
    /// </remarks>
    [Fact]
    public void Der_Anzeigenkontext_traegt_genau_fuenf_Felder()
    {
        Felder(typeof(Anzeigenentwurf)).Should().BeEquivalentTo(
            ["Titel", "Beschreibung", "Faehigkeiten", "Ort", "Wunsch", "Prompt"],
            "ADR-0024 §3, Gegenstück — hier fehlt bewusst der Mandant");
    }

    /// <summary>
    /// Und die beiden bleiben getrennt: kein gemeinsamer Basistyp, keine
    /// gemeinsame Schnittstelle.
    /// </summary>
    /// <remarks>
    /// Der Zusammenzug beginnt nie mit einem gemeinsamen Prompt, sondern mit
    /// „die haben doch beide Faehigkeiten und Wunsch". Danach steht ein
    /// <c>if</c> in den Regeln, und das ist die Stelle, an der eine Regel für die
    /// falsche Seite gilt.
    /// </remarks>
    [Fact]
    public void Die_beiden_Entwuerfe_teilen_keinen_Typ()
    {
        var profil = typeof(Entwurfslage);
        var anzeige = typeof(Anzeigenentwurf);

        profil.BaseType.Should().Be<object>("kein gemeinsamer Basistyp");
        anzeige.BaseType.Should().Be<object>("kein gemeinsamer Basistyp");

        // Nicht „gar keine Schnittstelle": ein Record bringt IEquatable<T> selbst
        // mit, und das ist je Typ eine andere. Geprueft wird die Schnittmenge —
        // die Zusage lautet, dass die beiden nichts TEILEN.
        profil.GetInterfaces().Intersect(anzeige.GetInterfaces())
            .Should().BeEmpty("die beiden Entwuerfe teilen keine Schnittstelle");
    }

    /// <summary>Die öffentlichen Instanzeigenschaften eines Records.</summary>
    /// <remarks>
    /// Statische bleiben draußen: <c>Regeln</c> ist der System-Prompt und kein
    /// Feld, das jemand befüllt.
    /// <para>
    /// <c>Prompt</c> zählt dagegen mit, obwohl es abgeleitet ist — es ist die
    /// Zeichenkette, die wirklich hinausgeht. Ein neues Eingabefeld taucht damit
    /// zweimal auf: als Feld und, sobald jemand es einbaut, in dem, was gesendet
    /// wird. Beides soll auffallen.
    /// </para>
    /// </remarks>
    private static string[] Felder(Type typ) =>
        [.. typ.GetProperties(BindingFlags.Public | BindingFlags.Instance)
              .Select(e => e.Name)];
}
