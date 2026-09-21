namespace WorkerTransfer.Resume.Domain.Lebenslaeufe;

/// <summary>In welcher ResumeTemplate der CurriculumVitae gesetzt wird.</summary>
/// <remarks>
/// <strong>Ein Wert, kein Layout.</strong> Hier steht nur der Name; wie es
/// aussieht, liegt in <c>web/src/styles/</c> und gilt für beide Seiten — die
/// Person sieht dasselbe wie das Unternehmen, dem sie den CurriculumVitae schickt
/// (ADR-0035). Ein Server, der Layout kennt, müsste es zweimal kennen, und die
/// zweite Fassung wäre beim ersten Feld falsch.
/// <para>
/// Deshalb gibt es hier auch keine Farben, keine Schriftgrößen und keine
/// Reihenfolge von Abschnitten: alles davon wäre Gestaltung im falschen Haus.
/// </para>
/// </remarks>
public enum ResumeTemplate
{
    /// <summary>Ohne Zierde. Die Vorgabe.</summary>
    Plain,

    /// <summary>Zweispaltig, mit Kopfzeile.</summary>
    Classic,

    /// <summary>Kräftige Überschriften, viel Weißraum.</summary>
    Modern
}
