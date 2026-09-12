namespace WorkerTransfer.Profile.Domain.Pruefspur;

/// <summary>Was geschehen ist, wie die Prüfspur es festhält.</summary>
/// <remarks>
/// Eine geschlossene Menge und ausdrücklich keine Freitextspalte: eine Spur,
/// deren Vokabular an der Aufrufstelle wachsen kann, ist Jahre später nicht
/// lesbar, ohne den Code zu lesen, der sie geschrieben hat.
/// <para>
/// <b>Es gibt keine Handlung fürs Lesen</b>, und das ist die eine Entscheidung
/// dieser Datei. Eine Zeile „Unternehmen X hat Person Y angesehen“ wäre genau
/// die Überwachungsspur, die ein einwilligungsgeführtes System nicht führen
/// will: sie entstünde ohne Zutun der Person, sie stünde jahrelang bei uns, und
/// sie beantwortete Fragen, die niemand stellen darf. Rekonstruiert werden muss
/// sie auch nicht — ein Lesen entscheidet nichts, es zeigt nur, was der Ledger
/// im selben Augenblick erlaubt hat.
/// </para>
/// <para>
/// Jedes Mitglied wird als <c>snake_case</c> seines Namens gespeichert — das
/// ist Npgsqls Regel und nicht unsere. <b>Ein Test dafür gibt es hier nicht</b>,
/// anders als in identity-service (<c>SpaltenetikettenTests</c>); dieser
/// Kommentar behauptete lange einen, den niemand geschrieben hatte. Wer ein
/// viertes Mitglied ergänzt, ohne eine Wanderung dafür anzulegen, merkt es
/// deshalb erst beim ersten Einfügen.
/// </para>
/// </remarks>
public enum Pruefhandlung
{
    /// <summary>Jemand hat sein Profil zum ersten Mal angelegt.</summary>
    ProfilAngelegt,

    /// <summary>Jemand hat sein Profil geändert.</summary>
    ProfilGeaendert,

    /// <summary>Ein Profil ist der Löschkaskade gefolgt.</summary>
    ProfilGeloescht
}
