using Girder.Core.Identity;

namespace WorkerTransfer.Advisor.Domain.Gespraeche;

/// <summary>Wie weit ein Gespräch geöffnet ist.</summary>
/// <remarks>
/// <para><strong>Keine Spalte, nirgends.</strong> Die Stufe wird bei jedem Lesen
/// aus dem Ledger abgeleitet und niemals gespeichert (ADR-0037 Entscheidung 2,
/// ADR-0020). Eine <c>stufe</c>-Spalte wäre eine zweite Wahrheit über
/// Sichtbarkeit — und sie liefe beim ersten Widerruf auseinander, also genau
/// dort, wo jemand sichtbar bliebe, der es nicht mehr sein wollte.</para>
///
/// <para><strong><see cref="Keine"/> ist ein Zustand und keine Lücke.</strong>
/// Wer nichts (mehr) freigegeben hat, für den existiert das Gespräch auf der
/// Gegenseite nicht: es taucht in keiner Liste des Unternehmens auf und
/// antwortet auf direktem Weg 404 — dieselbe 404 wie „gibt es nicht".</para>
/// </remarks>
public enum Stufe
{
    /// <summary>Nichts freigegeben. Für die Gegenseite gibt es das Gespräch nicht.</summary>
    Keine = 0,

    /// <summary>Profil, genannte Fähigkeiten, Verfügbarkeit.</summary>
    Profil = 1,

    /// <summary>Dazu Belege, Lebenslauf und die Gehaltsspanne.</summary>
    Unterlagen = 2,

    /// <summary>Dazu Klarname und Kontakt.</summary>
    Person = 3
}

/// <summary>
/// Die Fähigkeiten, aus denen eine Stufe besteht — und die einzige Stelle, an
/// der sie stehen.
/// </summary>
/// <remarks>
/// <para><strong>Es sind die bestehenden Sichtbarkeiten, nicht drei neue</strong>
/// (ADR-0037 Entscheidung 2). Eine zweite Fähigkeit für dasselbe („Profil
/// sichtbar für Firma X") wäre eine zweite Wahrheit, und die weicht beim ersten
/// Widerruf ab. Neu ist genau <em>eine</em>: <see cref="Klarname"/>, weil es für
/// „bürgerlicher Name und Kontakt" bisher keine gab.</para>
///
/// <para><strong>Und sie trägt keine Ziffer.</strong> <c>advisor.stage1</c>
/// würde <c>Capability</c> ablehnen — der Namensraum lässt keine Ziffern zu.
/// Das ist kein Zufall der Regex, sondern der Grund, hier nicht zu nummerieren:
/// eine Fähigkeit sagt, <em>was</em> sichtbar wird, nie <em>wie weit</em>
/// jemand gekommen ist.</para>
///
/// <para><strong>Zwei Mengen je Stufe, und der Unterschied trägt das Design.</strong>
/// <see cref="Traegt"/> ist die Fähigkeit, an der eine Stufe <em>steht</em>;
/// <see cref="Erteilt"/> ist, was eine Freigabe <em>schreibt</em>. Sie fallen
/// bei Stufe 1 und 2 auseinander, weil eine Stufe mehr verspricht als eine
/// Zeile: Stufe 1 verspricht auch die Verfügbarkeit (Marktstatus), Stufe 2 auch
/// die Zeugnisse. Wer eines davon einzeln zurücknimmt, verliert genau dieses —
/// die Stufe selbst hängt an der Zeile, die sie ausmacht.</para>
/// </remarks>
public static class Stufenfaehigkeiten
{
    /// <summary>„Für alle Unternehmen" — der Schalter auf der Profilseite.</summary>
    /// <remarks>
    /// Gelesen, nie geschrieben: eine Stufenfreigabe gilt <em>einem</em>
    /// Unternehmen. Wer schon öffentlich sichtbar ist, steht damit auch in
    /// Stufe 1 — sonst behauptete dieser Dienst eine Verborgenheit, die es
    /// nicht gibt.
    /// </remarks>
    public const string ProfilOeffentlich = "profile.visibility:public";

    /// <summary>Das Profil, für dieses eine Unternehmen.</summary>
    public static string Profil(TenantId firma) => $"profile.visibility:tenant:{firma.Value}";

    /// <summary>
    /// Der Marktstatus, für dieses eine Unternehmen.
    /// </summary>
    /// <remarks>
    /// Kein <c>:public</c>, und es darf keines geben — die Freigabe des
    /// Marktstatus nennt immer einen Empfänger (transfer-service sagt warum).
    /// Stufe 1 nennt genau einen.
    /// </remarks>
    public static string Markt(TenantId firma) => $"market.visibility:tenant:{firma.Value}";

    /// <summary>Der Lebenslauf, für dieses eine Unternehmen.</summary>
    public static string Lebenslauf(TenantId firma) => $"resume.visibility:tenant:{firma.Value}";

    /// <summary>Die Zeugnisse, für dieses eine Unternehmen.</summary>
    public static string Unterlagen(TenantId firma) => $"documents.visibility:tenant:{firma.Value}";

    /// <summary>
    /// Bürgerlicher Name und Kontakt, für dieses eine Unternehmen.
    /// </summary>
    /// <remarks>
    /// Die einzige neue Fähigkeit dieses Dienstes. Sie ist — wie jede andere im
    /// System — eine <em>Sichtbarkeit</em>: „dieses Unternehmen darf meinen
    /// Klarnamen sehen". Deshalb kann „lösche <c>advisor.identity:tenant:…</c>"
    /// nie „lösche die Person" heißen (ADR-0027 §1).
    /// </remarks>
    public static string Klarname(TenantId firma) => $"advisor.identity:tenant:{firma.Value}";

    /// <summary>Woran eine Stufe steht — eine Fähigkeit je Stufe.</summary>
    /// <remarks>
    /// Stufe 1 steht an <em>zwei</em> Möglichkeiten, weil „für alle" die
    /// weitere ist: wer öffentlich sichtbar ist, ist es auch für dieses
    /// Unternehmen.
    /// </remarks>
    public static IReadOnlyList<string> Traegt(Stufe stufe, TenantId firma) => stufe switch
    {
        Stufe.Profil => [Profil(firma), ProfilOeffentlich],
        Stufe.Unterlagen => [Lebenslauf(firma)],
        Stufe.Person => [Klarname(firma)],
        _ => []
    };

    /// <summary>Was eine Freigabe dieser Stufe schreibt.</summary>
    /// <remarks>
    /// <para>Stufe 1 schreibt auch den Marktstatus, weil sie „Verfügbarkeit"
    /// verspricht und die Verfügbarkeit <em>der</em> Marktstatus ist. Eine
    /// Stufe, die drei Dinge zusagt und zwei stillschweigend liefert, wäre
    /// genau der „gesperrt"-Hinweis, den dieses Design nicht gibt — nur
    /// andersherum.</para>
    ///
    /// <para><strong>Ansprechbarkeit erteilt sie damit nicht.</strong>
    /// <c>unavailable</c> heißt weiterhin nein, auch mit Freigabe: die Freigabe
    /// erlaubt zu sehen, nicht zu stören. Diese Regel steht in
    /// transfer-service und wird hier nicht nachgebaut.</para>
    ///
    /// <para><strong><c>github.visibility:public</c> steht bewusst nicht hier.</strong>
    /// Sie ist plattformweit und kennt keine Firmenfassung; sie in einer
    /// Stufenfreigabe mitzuschreiben hieße, aus einer Freigabe an <em>ein</em>
    /// Unternehmen eine an alle zu machen. Belege reisen zu Stufe 2 mit, wenn
    /// die Person sie ohnehin öffentlich gestellt hat — erteilt werden sie hier
    /// nie.</para>
    /// </remarks>
    public static IReadOnlyList<string> Erteilt(Stufe stufe, TenantId firma) => stufe switch
    {
        Stufe.Profil => [Profil(firma), Markt(firma)],
        Stufe.Unterlagen => [Lebenslauf(firma), Unterlagen(firma)],
        Stufe.Person => [Klarname(firma)],
        _ => []
    };

    /// <summary>Die drei Stufen, von unten nach oben.</summary>
    public static readonly IReadOnlyList<Stufe> Alle =
    [
        Stufe.Profil, Stufe.Unterlagen, Stufe.Person
    ];

    /// <summary>Das Wort zur Stufe, auf dem Draht.</summary>
    public static int Zahl(Stufe stufe) => (int)stufe;

    /// <summary>Die Stufe zur Zahl, oder <c>null</c>.</summary>
    public static Stufe? Lies(int? zahl) => zahl switch
    {
        1 => Stufe.Profil,
        2 => Stufe.Unterlagen,
        3 => Stufe.Person,
        _ => null
    };
}
