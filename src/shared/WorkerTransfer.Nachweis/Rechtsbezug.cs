namespace WorkerTransfer.Nachweis;

/// <summary>Das Regelwerk, aus dem ein Artikel stammt.</summary>
/// <remarks>
/// <strong>Souveränität steht hier nicht.</strong> Keine Verordnung verlangt
/// sie; sie ist eine Entscheidung des Betreibers. Wo der Nachweis davon
/// spricht — die Egress-Grenze, der eigene Anbieter im eigenen Netz —, misst er
/// diese Entscheidung und nicht die Übereinstimmung mit einer Regel. Ein
/// Eintrag hier würde aus einer Haltung eine Pflicht machen, die niemand
/// geschrieben hat.
/// </remarks>
public enum Regelwerk
{
    /// <summary>Verordnung (EU) 2016/679.</summary>
    Dsgvo,

    /// <summary>Verordnung (EU) 2024/1689 — die KI-Verordnung.</summary>
    KiVo,

    /// <summary>Richtlinie (EU) 2022/2555.</summary>
    Nis2,

    /// <summary>Verordnung (EU) 2022/2554.</summary>
    Dora,

    /// <summary>Betriebsverfassungsgesetz.</summary>
    BetrVG
}

/// <summary>Ein Artikel, wonach er fragt — und was danach ein Mensch entscheidet.</summary>
/// <remarks>
/// <para><strong>Die Linie: Belege, keine Konformität.</strong> Ein Programm
/// kann zeigen, dass etwas vorhanden ist, wann es entstanden ist und dass es
/// unverändert ist. <em>Angemessenheit</em> kann es nicht zeigen. Deshalb sagt
/// kein Feld dieses Typs „erfüllt“ — <see cref="Pflicht"/> sagt, wonach der
/// Artikel fragt, und <see cref="Leser"/> sagt, was offenbleibt.</para>
///
/// <para><strong><see cref="Leser"/> ist nie leer, und zwar als Test.</strong>
/// Ein Zitat, das seinen eigenen Artikel erledigte, wäre genau die Anmaßung,
/// gegen die dieses Vokabular existiert. Nach Art. 42/43 DSGVO darf nur eine
/// Aufsichtsbehörde oder eine nach EN ISO/IEC 17065 akkreditierte Stelle
/// zertifizieren; „zertifiziert“ ohne Akkreditierung ist in der EU eine
/// irreführende Geschäftspraxis (RL 2005/29/EG, RL 2006/114/EG). Das ist ein
/// echtes Risiko, kein theoretisches.</para>
///
/// <para><strong><see cref="Gilt"/> trägt das Datum, wo eines gilt.</strong>
/// Die KI-VO läuft gestaffelt an: Art. 50 seit dem 02.08.2026, Art. 12 und
/// Art. 26 erst ab dem 02.12.2027. Ein Dokument, das eine Pflicht von 2027 so
/// darstellt, als binde sie heute, lädt den Leser ein, zu früh Geld
/// auszugeben.</para>
/// </remarks>
/// <param name="Regelwerk">Woher der Artikel stammt.</param>
/// <param name="Artikel">Seine Bezeichnung, wie man sie nachschlägt.</param>
/// <param name="Pflicht">Wonach er fragt — nie, ob wir ihn erfüllen.</param>
/// <param name="Leser">
/// Was ein Mensch danach noch entscheidet. <strong>Nie leer.</strong>
/// </param>
/// <param name="Gilt">
/// Seit oder ab wann die Pflicht bindet, wo das nicht selbstverständlich ist.
/// Leer heißt: sie gilt und hat kein Anlaufdatum, das jemand kennen müsste.
/// </param>
public sealed record Rechtsbezug(
    Regelwerk Regelwerk,
    string Artikel,
    string Pflicht,
    string Leser,
    string Gilt = "")
{
    /// <summary>Wie ein Mensch den Artikel nennt, samt Regelwerk.</summary>
    public string Fundstelle => $"{Wort(Regelwerk)} {Artikel}";

    /// <summary>Das Regelwerk, ausgeschrieben wie in einem Schriftsatz.</summary>
    /// <param name="regelwerk">Welches.</param>
    /// <returns>Seine übliche Abkürzung.</returns>
    public static string Wort(Regelwerk regelwerk) => regelwerk switch
    {
        Regelwerk.Dsgvo => "DSGVO",
        Regelwerk.KiVo => "KI-VO",
        Regelwerk.Nis2 => "NIS-2",
        Regelwerk.Dora => "DORA",
        Regelwerk.BetrVG => "BetrVG",
        _ => regelwerk.ToString()
    };
}
