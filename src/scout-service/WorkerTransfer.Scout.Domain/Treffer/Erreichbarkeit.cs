using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Scout.Domain.Treffer;

/// <summary>Wie weit jemand zu pendeln bereit ist — die Stufe, nie eine Zahl.</summary>
/// <remarks>
/// Dieselben fünf Stufen wie am Profil, und derselbe Grund: eine Zahl lädt zum
/// Rechnen ein, eine Stufe ist eine Aussage. Sie kommt über die interne Suche
/// als Wort herein und wird hier gelesen, nicht gerechnet.
/// </remarks>
public enum Pendelstufe
{
    /// <summary>Bis zehn Kilometer.</summary>
    Bis10,

    /// <summary>Bis fünfundzwanzig.</summary>
    Bis25,

    /// <summary>Bis fünfzig.</summary>
    Bis50,

    /// <summary>Bis hundert.</summary>
    Bis100,

    /// <summary>Entfernung spielt keine Rolle.</summary>
    Egal
}

/// <summary>Welche Anwesenheit eine Stelle verlangt.</summary>
/// <remarks>
/// <para><strong>Dieselbe Dreiteilung, die jobs-service seit jeher als
/// <c>Remotegrad</c> führt</strong> — und genau deshalb entsteht dort kein
/// zweites Feld. ADR-0041 §2 verlangte „ein Feld an der Anzeige:
/// remote / hybrid / vor_ort"; es gab es schon, unter anderem Namen und mit
/// derselben Begründung im Quelltext: <em>„hybrid ist der häufigste Fall und
/// keine Zwischenstufe von wahr"</em>. Ein zweites wäre eine zweite Wahrheit
/// über dieselbe Frage gewesen.</para>
/// </remarks>
public enum Anwesenheit
{
    /// <summary>Ganz remote.</summary>
    Remote,

    /// <summary>Teilweise vor Ort — der häufigste Fall (3+2).</summary>
    Hybrid,

    /// <summary>Ganz vor Ort.</summary>
    VorOrt
}

/// <summary>Was über die Erreichbarkeit gesagt werden kann — drei Zustände.</summary>
/// <remarks>
/// <para><strong>Der dritte ist so wichtig wie die ersten beiden.</strong> Wer
/// nichts gesagt hat, bekommt kein Nein: „nichts gesagt" ist nicht „passt
/// nicht" (ADR-0041, ADR-0022 §3). Dasselbe gilt, wenn wir den Ort nicht
/// auflösen können — dann fehlt die Auskunft bei <em>uns</em>, und das ist erst
/// recht keine Aussage über den Menschen.</para>
/// </remarks>
public enum Erreichbarkeitsstand
{
    /// <summary>Die Stelle liegt innerhalb dessen, was die Person gesagt hat.</summary>
    Erreichbar,

    /// <summary>Sie liegt darüber — und der Treffer bleibt trotzdem in der Liste.</summary>
    Weiter,

    /// <summary>Niemand hat etwas gesagt, oder ein Ort ist unbekannt.</summary>
    Ungesagt
}

/// <summary>
/// Das Häkchen zur Entfernung — und alles, was daneben stehen darf.
/// </summary>
/// <param name="Stand">Ja, Nein oder Strich.</param>
/// <param name="Stufe">Was die Person gesagt hat, oder <c>null</c>.</param>
/// <param name="Anwesenheit">Was die Stelle verlangt, oder <c>null</c>.</param>
/// <remarks>
/// <para><strong>Keine Kilometerzahl.</strong> Die Entfernung wird gerechnet und
/// sofort auf einen der drei Zustände reduziert; sie steht in keinem Feld dieses
/// Records und reist nie in einen Vertrag. Eine Zahl hier sähe aus wie eine
/// Messung, wäre aber der Abstand zweier Stadtmittelpunkte — und sobald zwei
/// davon untereinanderstehen, ordnet sie Menschen (ADR-0022).</para>
///
/// <para>Was danebensteht, sind die beiden <em>Aussagen</em>: „sagt: bis 50 km"
/// und „Stelle: hybrid". Beide hat jemand getroffen, beide kann man
/// widersprechen — anders als einer Zahl.</para>
/// </remarks>
public sealed record Erreichbarkeit(
    Erreichbarkeitsstand Stand,
    Pendelstufe? Stufe,
    Anwesenheit? Anwesenheit)
{
    /// <summary>Wenn nichts zu sagen ist.</summary>
    public static Erreichbarkeit Ungesagt { get; } =
        new(Erreichbarkeitsstand.Ungesagt, null, null);

    /// <summary>
    /// Bildet das Häkchen aus zwei Aussagen und zwei Orten.
    /// </summary>
    /// <remarks>
    /// <para><strong>Die Reihenfolge der Ausstiege ist die Aussage.</strong> Es
    /// gibt vier Wege zu <see cref="Erreichbarkeitsstand.Ungesagt"/>, und keiner
    /// davon darf zu einem Nein werden: keine Stufe genannt, keine Stelle
    /// genannt, ein Ort nicht auflösbar — und der Sonderfall, dass die Stelle
    /// ganz remote ist.</para>
    ///
    /// <para><strong>Remote ist ein Ja, kein Strich</strong>: wer nicht kommen
    /// muss, erreicht die Stelle, egal wie weit er wohnt. Das ist der einzige
    /// Ausstieg, der ohne Ortskenntnis ein <em>Ja</em> geben darf.</para>
    ///
    /// <para><strong>Hybrid zählt wie vor Ort</strong>, und das ist eine
    /// Entscheidung: bei 3+2 fährt jemand drei Tage die Woche. Die Strecke
    /// halbieren, weil „nur zwei Tage Homeoffice", wäre eine Rechnung über einen
    /// Menschen, die niemand begründet hat.</para>
    /// </remarks>
    /// <param name="stufe">Was die Person gesagt hat, oder <c>null</c>.</param>
    /// <param name="anwesenheit">Was die Stelle verlangt, oder <c>null</c>.</param>
    /// <param name="beiDerPerson">Der aufgelöste Ort der Person, oder <c>null</c>.</param>
    /// <param name="beiDerStelle">Der aufgelöste Ort der Stelle, oder <c>null</c>.</param>
    public static Erreichbarkeit Bilde(
        Pendelstufe? stufe,
        Anwesenheit? anwesenheit,
        Ortspunkt? beiDerPerson,
        Ortspunkt? beiDerStelle)
    {
        if (anwesenheit is null)
        {
            return Ungesagt;
        }

        if (anwesenheit is Domain.Treffer.Anwesenheit.Remote)
        {
            // Wer nicht kommen muss, erreicht die Stelle. Die Stufe der Person
            // steht trotzdem daneben — sie ist eine Auskunft, kein Vorbehalt.
            return new Erreichbarkeit(
                Erreichbarkeitsstand.Erreichbar, stufe, anwesenheit);
        }

        if (stufe is null)
        {
            return new Erreichbarkeit(Erreichbarkeitsstand.Ungesagt, null, anwesenheit);
        }

        if (stufe is Pendelstufe.Egal)
        {
            return new Erreichbarkeit(
                Erreichbarkeitsstand.Erreichbar, stufe, anwesenheit);
        }

        if (beiDerPerson is not { } person || beiDerStelle is not { } stelle)
        {
            // Ein unbekannter Ort ist ein Loch bei UNS. Daraus ein Kreuz zu
            // machen hiesse, unsere Ortstabelle zu einer Aussage ueber einen
            // Menschen zu erklaeren.
            return new Erreichbarkeit(Erreichbarkeitsstand.Ungesagt, stufe, anwesenheit);
        }

        var grenze = Grenze(stufe.Value);
        var entfernung = Ortskunde.EntfernungKm(person, stelle);

        return new Erreichbarkeit(
            entfernung <= grenze
                ? Erreichbarkeitsstand.Erreichbar
                : Erreichbarkeitsstand.Weiter,
            stufe,
            anwesenheit);
    }

    /// <summary>
    /// Wie viele Kilometer eine Stufe deckt.
    /// </summary>
    /// <remarks>
    /// <strong>Privat, und das ist der Punkt.</strong> Diese Zahl wird an genau
    /// einer Stelle gelesen — beim Vergleich eine Zeile weiter oben — und
    /// verlässt den Typ nie. Sie gehört in keinen Vertrag, in keine Antwort und
    /// in keine Sortierung (ADR-0041).
    /// </remarks>
    private static double Grenze(Pendelstufe stufe) => stufe switch
    {
        Pendelstufe.Bis10 => 10,
        Pendelstufe.Bis25 => 25,
        Pendelstufe.Bis50 => 50,
        Pendelstufe.Bis100 => 100,
        _ => double.MaxValue
    };
}

/// <summary>Die Worte, mit denen die Stufen und die Anwesenheit auf dem Draht stehen.</summary>
/// <remarks>
/// Dieselben Worte, die profile-service schreibt und jobs-service meldet. Zwei
/// Schreibweisen für dieselbe Stufe wären zwei Gelegenheiten, eine Aussage
/// stillschweigend fallen zu lassen — und sie fiele als Strich aus, nicht als
/// Fehler.
/// </remarks>
public static class Erreichbarkeitsworte
{
    /// <summary>Die Stufe zum Wort, oder <c>null</c>.</summary>
    public static Pendelstufe? Stufe(string? wort) => wort switch
    {
        "bis_10" => Pendelstufe.Bis10,
        "bis_25" => Pendelstufe.Bis25,
        "bis_50" => Pendelstufe.Bis50,
        "bis_100" => Pendelstufe.Bis100,
        "egal" => Pendelstufe.Egal,
        _ => null
    };

    /// <summary>Das Wort zur Stufe, für die Antwort.</summary>
    public static string? Wort(Pendelstufe? stufe) => stufe switch
    {
        Pendelstufe.Bis10 => "bis_10",
        Pendelstufe.Bis25 => "bis_25",
        Pendelstufe.Bis50 => "bis_50",
        Pendelstufe.Bis100 => "bis_100",
        Pendelstufe.Egal => "egal",
        _ => null
    };

    /// <summary>
    /// Die Anwesenheit zum Wort von jobs-service, oder <c>null</c>.
    /// </summary>
    /// <remarks>
    /// <c>none</c>, <c>hybrid</c>, <c>full</c> — so meldet <c>StelleV1</c> den
    /// <c>Remotegrad</c>, und so wird er hier gelesen. Die Worte werden NICHT
    /// umbenannt: ein zweiter Wortschatz für dieselbe Sache ginge beim ersten
    /// neuen Wert auseinander.
    /// </remarks>
    public static Anwesenheit? Anwesenheit(string? wort) => wort switch
    {
        "none" => Domain.Treffer.Anwesenheit.VorOrt,
        "hybrid" => Domain.Treffer.Anwesenheit.Hybrid,
        "full" => Domain.Treffer.Anwesenheit.Remote,
        _ => null
    };

    /// <summary>Das Wort zur Anwesenheit, für die Antwort.</summary>
    public static string? Wort(Anwesenheit? anwesenheit) => anwesenheit switch
    {
        Domain.Treffer.Anwesenheit.VorOrt => "vor_ort",
        Domain.Treffer.Anwesenheit.Hybrid => "hybrid",
        Domain.Treffer.Anwesenheit.Remote => "remote",
        _ => null
    };

    /// <summary>Das Wort zum Stand, für die Antwort.</summary>
    public static string Wort(Erreichbarkeitsstand stand) => stand switch
    {
        Erreichbarkeitsstand.Erreichbar => "reachable",
        Erreichbarkeitsstand.Weiter => "further",
        Erreichbarkeitsstand.Ungesagt => "unsaid",
        _ => throw new ArgumentOutOfRangeException(nameof(stand))
    };
}
