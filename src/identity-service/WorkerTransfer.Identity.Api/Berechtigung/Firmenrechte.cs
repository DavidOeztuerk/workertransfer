namespace WorkerTransfer.Identity.Api.Berechtigung;

/// <summary>
/// Die Rechte, die eine Firmenverwaltung kennt.
/// </summary>
/// <remarks>
/// Namen, keine Rollen. Ein Endpunkt sagt damit, <em>wozu</em> er berechtigt
/// sein will, und nicht, <em>wer</em> das ist — wer es ist, entscheidet
/// <see cref="Mitgliedschaftsrecht"/> beim Lesen der Mitgliedschaft.
/// <para>
/// Der Präfix <c>Permission:</c> ist Girders: sein
/// <c>PermissionPolicyProvider</c> baut aus jedem so benannten Richtliniennamen
/// zur Laufzeit eine Richtlinie. Deshalb muss keine davon vorher angemeldet
/// werden — und deshalb ist es wichtig, dass der Anbieter überhaupt
/// registriert ist: ohne ihn beantwortet niemand diese Namen, und das Gerüst
/// lehnt <em>jede</em> Anfrage an den geschützten Endpunkt ab.
/// </para>
/// </remarks>
public static class Firmenrechte
{
    /// <summary>Girders Präfix für dynamisch aufgelöste Richtlinien.</summary>
    public const string Praefix = "Permission:";

    /// <summary>Jemanden in die eigene Firma einladen.</summary>
    public const string Einladen = "company.invite";

    /// <summary>Eine offene Einladung zuruecknehmen.</summary>
    /// <remarks>
    /// Die Kehrseite von <see cref="Einladen"/>, und deshalb dasselbe Recht
    /// wert: wer nicht einladen darf, darf auch nicht die Einladung einer
    /// Kollegin wegnehmen. Dass sie <em>gelesen</em> werden darf, ist eine
    /// andere Frage — <c>GET /invitations</c> bleibt jedem Mitglied offen, weil
    /// eine Firma ihrer eigenen Belegschaft nicht verschweigen muss, wen sie
    /// gerade sucht.
    /// </remarks>
    public const string EinladungZuruecknehmen = "company.invitations.withdraw";

    /// <summary>Ein Mitglied aus der eigenen Firma entfernen.</summary>
    public const string Entfernen = "company.members.remove";

    /// <summary>Der Richtlinienname zu einem Recht.</summary>
    public static string Richtlinie(string recht) => Praefix + recht;
}
