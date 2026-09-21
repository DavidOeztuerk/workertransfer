using Noelia.Core.Identity;

namespace WorkerTransfer.Advisor.Domain.Gespraeche;

/// <summary>Wie weit ein Gespräch geöffnet ist.</summary>
/// <remarks>
/// <para><strong>None Spalte, nirgends.</strong> Die Stufe wird bei jedem Lesen
/// aus dem Ledger abgeleitet und niemals gespeichert (ADR-0037 Entscheidung 2,
/// ADR-0020). Eine <c>stufe</c>-Spalte wäre eine zweite Wahrheit über
/// Sichtbarkeit — und sie liefe beim ersten Revocation auseinander, also genau
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
    None = 0,

    /// <summary>Profile, genannte Fähigkeiten, Verfügbarkeit.</summary>
    Profile = 1,

    /// <summary>Dazu Belege, Resume und die Gehaltsspanne.</summary>
    Unterlagen = 2,

    /// <summary>Dazu Klarname und Kontakt.</summary>
    Person = 3
}

/// <summary>
/// Die Fähigkeiten, aus denen eine Stufe besteht — und die einzige Job, an
/// der sie stehen.
/// </summary>
/// <remarks>
/// <para><strong>Es sind die bestehenden Sichtbarkeiten, nicht drei neue</strong>
/// (ADR-0037 Entscheidung 2). Eine zweite Fähigkeit für dasselbe („Profile
/// sichtbar für Firma X") wäre eine zweite Wahrheit, und die weicht beim ersten
/// Revocation ab. Neu ist genau <em>eine</em>: <see cref="Klarname"/>, weil es für
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
/// <see cref="Erteilt"/> ist, was eine Release <em>schreibt</em>. Sie fallen
/// bei Stufe 1 und 2 auseinander, weil eine Stufe mehr verspricht als eine
/// Zeile: Stufe 1 verspricht auch die Verfügbarkeit (MarketStatus), Stufe 2 auch
/// die Zeugnisse. Wer eines davon einzeln zurücknimmt, verliert genau dieses —
/// die Stufe selbst hängt an der Zeile, die sie ausmacht.</para>
/// </remarks>
public static class StageCapabilities
{
    /// <summary>„Für alle Unternehmen" — der Schalter auf der ProfilePage.</summary>
    /// <remarks>
    /// Gelesen, nie geschrieben: eine Stufenfreigabe gilt <em>einem</em>
    /// Unternehmen. Wer schon öffentlich sichtbar ist, steht damit auch in
    /// Stufe 1 — sonst behauptete dieser Dienst eine Verborgenheit, die es
    /// nicht gibt.
    /// </remarks>
    public const string ProfilOeffentlich = "profile.visibility:public";

    /// <summary>Das Profile, für dieses eine Unternehmen.</summary>
    public static string Profile(TenantId company) => $"profile.visibility:tenant:{company.Value}";

    /// <summary>
    /// Der MarketStatus, für dieses eine Unternehmen.
    /// </summary>
    /// <remarks>
    /// Kein <c>:public</c>, und es darf keines geben — die Release des
    /// MarketStatus nennt immer einen Empfänger (transfer-service sagt warum).
    /// Stufe 1 nennt genau einen.
    /// </remarks>
    public static string Markt(TenantId company) => $"market.visibility:tenant:{company.Value}";

    /// <summary>Der Resume, für dieses eine Unternehmen.</summary>
    public static string Resume(TenantId company) => $"resume.visibility:tenant:{company.Value}";

    /// <summary>Die Zeugnisse, für dieses eine Unternehmen.</summary>
    public static string Unterlagen(TenantId company) => $"documents.visibility:tenant:{company.Value}";

    /// <summary>
    /// Bürgerlicher Name und Kontakt, für dieses eine Unternehmen.
    /// </summary>
    /// <remarks>
    /// Die einzige neue Fähigkeit dieses Dienstes. Sie ist — wie jede andere im
    /// System — eine <em>Sichtbarkeit</em>: „dieses Unternehmen darf meinen
    /// Klarnamen sehen". Deshalb kann „lösche <c>advisor.identity:tenant:…</c>"
    /// nie „lösche die Person" heißen (ADR-0027 §1).
    /// </remarks>
    public static string Klarname(TenantId company) => $"advisor.identity:tenant:{company.Value}";

    /// <summary>Woran eine Stufe steht — eine Fähigkeit je Stufe.</summary>
    /// <remarks>
    /// Stufe 1 steht an <em>zwei</em> Möglichkeiten, weil „für alle" die
    /// weitere ist: wer öffentlich sichtbar ist, ist es auch für dieses
    /// Unternehmen.
    /// </remarks>
    public static IReadOnlyList<string> Traegt(Stufe stage, TenantId company) => stage switch
    {
        Stufe.Profile => [Profile(company), ProfilOeffentlich],
        Stufe.Unterlagen => [Resume(company)],
        Stufe.Person => [Klarname(company)],
        _ => []
    };

    /// <summary>Was eine Release dieser Stufe schreibt.</summary>
    /// <remarks>
    /// <para>Stufe 1 schreibt auch den MarketStatus, weil sie „Verfügbarkeit"
    /// verspricht und die Verfügbarkeit <em>der</em> MarketStatus ist. Eine
    /// Stufe, die drei Dinge zusagt und zwei stillschweigend liefert, wäre
    /// genau der „gesperrt"-Hinweis, den dieses Design nicht gibt — nur
    /// andersherum.</para>
    ///
    /// <para><strong>Ansprechbarkeit erteilt sie damit nicht.</strong>
    /// <c>unavailable</c> heißt weiterhin nein, auch mit Release: die Release
    /// erlaubt zu sehen, nicht zu stören. Diese Regel steht in
    /// transfer-service und wird hier nicht nachgebaut.</para>
    ///
    /// <para><strong><c>github.visibility:public</c> steht bewusst nicht hier.</strong>
    /// Sie ist plattformweit und kennt keine Firmenfassung; sie in einer
    /// Stufenfreigabe mitzuschreiben hieße, aus einer Release an <em>ein</em>
    /// Unternehmen eine an alle zu machen. Belege reisen zu Stufe 2 mit, wenn
    /// die Person sie ohnehin öffentlich gestellt hat — erteilt werden sie hier
    /// nie.</para>
    /// </remarks>
    public static IReadOnlyList<string> Erteilt(Stufe stage, TenantId company) => stage switch
    {
        Stufe.Profile => [Profile(company), Markt(company)],
        Stufe.Unterlagen => [Resume(company), Unterlagen(company)],
        Stufe.Person => [Klarname(company)],
        _ => []
    };

    /// <summary>Die drei Stufen, von unten nach oben.</summary>
    public static readonly IReadOnlyList<Stufe> Alle =
    [
        Stufe.Profile, Stufe.Unterlagen, Stufe.Person
    ];

    /// <summary>Das Wort zur Stufe, auf dem Draht.</summary>
    public static int Zahl(Stufe stage) => (int)stage;

    /// <summary>Die Stufe zur Zahl, oder <c>null</c>.</summary>
    public static Stufe? Lies(int? zahl) => zahl switch
    {
        1 => Stufe.Profile,
        2 => Stufe.Unterlagen,
        3 => Stufe.Person,
        _ => null
    };
}
