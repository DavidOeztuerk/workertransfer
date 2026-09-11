namespace WorkerTransfer.ServiceDefaults.Rollen;

/// <summary>Was jemand in einem Unternehmen ist.</summary>
/// <remarks>
/// Zwei Rollen und ein „nichts", mehr kennt die Mitgliedschaftstabelle. Es gibt
/// bewusst keine dritte Rolle und keine Vererbung: jede weitere wäre eine
/// Entscheidung darüber, wer wen aussperren kann, und die gehört nicht in eine
/// Aufzählung, sondern in ein ADR.
/// </remarks>
public enum Firmenrolle
{
    /// <summary>Gehört nicht dazu — oder das Unternehmen gibt es nicht.</summary>
    /// <remarks>
    /// Die beiden sind absichtlich derselbe Wert. Der Unterschied wäre eine
    /// Auskunft darüber, welche Unternehmen es gibt.
    /// </remarks>
    Keine,

    /// <summary>Darf für das Unternehmen handeln.</summary>
    Mitglied,

    /// <summary>Darf ausserdem binden: einladen, entfernen, veröffentlichen, anbieten.</summary>
    Admin
}

/// <summary>Die Schreibweise, in der die Rolle über den Draht reist.</summary>
/// <remarks>
/// Dieselben Wörter wie in der Mitgliedschaftstabelle (<c>admin</c>,
/// <c>member</c>). Eine zweite Schreibweise unterwegs wäre die Stelle, an der
/// ein Tippfehler still zu „kein Admin" wird — und ein stiller Rechteverlust
/// sieht aus wie eine richtige Ablehnung.
/// </remarks>
public static class Rollennamen
{
    /// <summary>Liest, was identity-service geschrieben hat.</summary>
    /// <param name="roh">Das Wort vom Draht, oder <c>null</c>.</param>
    public static Firmenrolle Lies(string? roh) => roh switch
    {
        "admin" => Firmenrolle.Admin,
        "member" => Firmenrolle.Mitglied,
        _ => Firmenrolle.Keine
    };
}
