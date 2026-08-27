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

    /// <summary>Ein Mitglied aus der eigenen Firma entfernen.</summary>
    public const string Entfernen = "company.members.remove";

    /// <summary>Der Richtlinienname zu einem Recht.</summary>
    public static string Richtlinie(string recht) => Praefix + recht;
}
