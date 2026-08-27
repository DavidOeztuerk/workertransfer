namespace WorkerTransfer.ServiceDefaults;

/// <summary>„Diese Zeile handelt von einem Menschen."</summary>
/// <remarks>
/// Meistens ist das an der Spalte zu sehen: <c>subject_id</c> oder
/// <c>user_id</c>. Bei manchen Aggregaten <strong>ist</strong> der Schlüssel
/// aber die Person — <c>profiles.id</c>, <c>portfolios.id</c>,
/// <c>github_connections.id</c> —, und dann heißt die Spalte schlicht
/// <c>id</c>.
/// <para>
/// Eine Prüfung, die nur nach Spaltennamen sucht, übersieht genau diese
/// Tabellen und damit ganze Dienste in der Löschkaskade (ADR-0027 §4). Deshalb
/// wird es <em>gesagt</em> statt geraten, und zwar dort, wo die Tabelle
/// entsteht.
/// </para>
/// </remarks>
public static class Personenzeile
{
    /// <summary>Der Name der Anmerkung am Entitätstyp.</summary>
    public const string Anmerkung = "WorkerTransfer:Personenzeile";
}
