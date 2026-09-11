using Girder.Core.Identity;

namespace WorkerTransfer.Scout.Domain.Treffer;

/// <summary>Ein Haken: wurde dieses Wort von der Person genannt?</summary>
/// <remarks>
/// <para><strong>Ein Ja oder ein Nein je Wort — und nirgends eine Summe
/// darüber.</strong> „2 von 3" wäre eine Zahl über einen Menschen (ADR-0022,
/// ADR-0036 Auflage 2), und sie verbärge genau das Einzige, was hilft:
/// <em>welche</em> Fähigkeit fehlt. Ein Prozentwert erst recht — er sieht aus
/// wie eine Messung und ist eine Gewichtung, die niemand begründet hat.</para>
///
/// <para>Ein Haken ist ausserdem <em>widersprechbar</em>: er nennt ein Wort und
/// eine Antwort, und wer ihn für falsch hält, kann sagen warum. Eine Zahl kann
/// man nicht bestreiten.</para>
/// </remarks>
/// <param name="Wort">Das Wort, nach dem gesucht wurde — kanonisch.</param>
/// <param name="Genannt">Ob die Person es selbst in ihr Profil getippt hat.</param>
public sealed record Haken(string Wort, bool Genannt);

/// <summary>Woher ein Beleg kommt.</summary>
/// <remarks>
/// Ein Beleg ist eine Aussage über ein <em>Artefakt</em>, nie über einen
/// Menschen (ADR-0033). Die Herkunft steht deshalb dabei: wer den Beleg liest,
/// soll sehen, wer ihn geschrieben hat — ein Mensch an ein Repository, oder
/// GitHubs Sprachenerkennung an dessen Dateien.
/// </remarks>
public enum Belegart
{
    /// <summary>Ein Topic, das ein Mensch an das Repository geschrieben hat.</summary>
    /// <remarks>
    /// Der stärkste Beleg, weil er eine <em>Nennung</em> ist und nichts
    /// Abgeleitetes (ADR-0033).
    /// </remarks>
    Thema,

    /// <summary>Eine Sprache, die in dem Repository vorkommt.</summary>
    /// <remarks>
    /// Die Menge, nie der Anteil. GitHub meldet je Sprache eine Byte-Zahl, und
    /// genau daraus rechnete das gelöschte Paket sein „Können" (ADR-0022 §2).
    /// Die Zahlen reisen nirgends mit.
    /// </remarks>
    Sprache
}

/// <summary>Ein einzelner Beleg mit seiner Herkunft.</summary>
/// <param name="Wort">Das Topic oder der Sprachname, abgeschrieben.</param>
/// <param name="Art">Wer es gesagt hat.</param>
/// <param name="Projekt">Das Repository, aus dem es stammt.</param>
/// <param name="Adresse">Der Link dorthin — ein Beleg ohne Nachprüfbarkeit ist keiner.</param>
public sealed record Beleg(string Wort, Belegart Art, string Projekt, string Adresse);

/// <summary>Was ein Treffer über seine eigene Unvollständigkeit sagt.</summary>
/// <remarks>
/// <para><strong>Die dritte Entscheidung aus ADR-0036, und sie ist die
/// unbequemste.</strong> Wer nichts auf GitHub hat, ist nicht schlechter,
/// sondern woanders (ADR-0022 §3). Ein Treffer ohne Belege bekommt deshalb
/// dieselbe Antwortgestalt wie jeder andere und einen <em>Hinweis in der
/// JSON</em> — keine leere Box, keine ausgegraute Karte, kein Weglassen.</para>
///
/// <para>Ein Wort und keine Sätze: die Oberfläche formuliert in der Sprache der
/// lesenden Person (ADR-0031), und ein deutscher Satz auf dem Draht wäre eine
/// Sprache, die der Server für alle festlegt.</para>
/// </remarks>
public enum Belegstand
{
    /// <summary>Belege liegen vor, und sie sind vollständig.</summary>
    Vollstaendig,

    /// <summary>Diese Person hat keine nachgewiesene, freigegebene Verbindung.</summary>
    /// <remarks>
    /// Das ist <em>kein</em> Urteil. Es heisst: hier steht nichts, und hier
    /// steht auch nicht, dass nichts wäre.
    /// </remarks>
    KeineFreigegeben,

    /// <summary>Es gibt Belege, aber nicht alle konnten geholt werden.</summary>
    Unvollstaendig,

    /// <summary>Der Belegdienst hat nicht geantwortet.</summary>
    /// <remarks>
    /// Ausdrücklich verschieden von <see cref="KeineFreigegeben"/>: „wir wissen
    /// es gerade nicht" ist etwas anderes als „es gibt nichts", und die beiden
    /// gleich zu benennen wäre eine Aussage über die Person, die aus unserem
    /// Ausfall stammt.
    /// </remarks>
    Unerreichbar
}

/// <summary>Ein Mensch, der gefunden wurde — mit Haken und Belegen, ohne Zahl.</summary>
/// <remarks>
/// <para>Was hier steht, hat die Person selbst geschrieben
/// (<see cref="Ueberschrift"/>, <see cref="Ort"/>, <see cref="Genannt"/>) oder
/// ist ein Beleg mit Herkunft und Link. Nichts ist gerechnet, nichts
/// zusammengefasst, nichts geordnet.</para>
///
/// <para><strong>Die Häkchenliste gehört in diesen Typ und nicht in die
/// Oberfläche allein</strong> (ADR-0036 Entscheidung 3). Läge sie nur dort,
/// rechnete der Browser sich eine Zahl aus den Rohdaten — und ADR-0022 wäre
/// durch die Hintertür da.</para>
/// </remarks>
public sealed record Treffer(
    SubjectId Wer,
    string Ueberschrift,
    string Ort,
    bool RemoteMoeglich,
    IReadOnlyList<string> Genannt,
    IReadOnlyList<Haken> Haken,
    IReadOnlyList<Beleg> Belege,
    Belegstand Belegstand);

/// <summary>Eine Seite Treffer.</summary>
/// <remarks>
/// <para><strong>Keine Gesamtzahl</strong> (ADR-0026, ADR-0036). Sie verriete
/// über die Differenz zur Seitenlänge, wie viele Profile <em>nicht</em>
/// freigegeben sind — genau die Auskunft, die der Ledger schützt.</para>
///
/// <para>Und <strong>keine Auffüllung einer kurzen Seite</strong>
/// (ADR-0020 §4): nachzuladen, bis die Seite voll ist, verriete dasselbe über
/// die Anzahl der Runden. Eine kurze Seite ist hier eine richtige Antwort.</para>
/// </remarks>
public sealed record Trefferseite(IReadOnlyList<Treffer> Eintraege, string? Weiter);
