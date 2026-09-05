namespace WorkerTransfer.Jobs.Domain.Stellen;

/// <summary>„Im Umkreis von N Kilometern um diesen Punkt."</summary>
/// <remarks>
/// <strong>Der Punkt kommt aus dem Browser und ist absichtlich ungenau.</strong>
/// Was hier ankommt, ist auf zwei Nachkommastellen gerundet — gut einen
/// Kilometer. Das ist keine Bequemlichkeit, sondern Datensparsamkeit: Anzeigen
/// tragen einen ORTSNAMEN, keine Hausnummer, und ihre Koordinaten sind die des
/// Stadtzentrums. Eine genauere Position der suchenden Person könnte an keiner
/// Antwort etwas ändern — sie stünde nur in den Zugriffsprotokollen.
/// <para>
/// Deshalb steht hier auch kein Wohnort und keine Adresse: es gibt nichts
/// dauerhaft Gespeichertes zu diesem Filter. Er lebt in einer Abfrage und
/// endet mit ihr.
/// </para>
/// </remarks>
public sealed record Umkreis
{
    /// <summary>Der kleinste sinnvolle Radius.</summary>
    /// <remarks>
    /// Zehn und nicht eins: die Koordinaten der Anzeigen sind Stadtmittelpunkte,
    /// und ein Radius unter der Ausdehnung einer Stadt behauptete eine Genauigkeit,
    /// die die Daten nicht haben.
    /// </remarks>
    public const double KleinsterRadiusKm = 10;

    /// <summary>Der grösste. Darüber ist es keine Umkreissuche mehr.</summary>
    public const double GroessterRadiusKm = 500;

    private Umkreis(double breite, double laenge, double radiusKm)
    {
        Breite = breite;
        Laenge = laenge;
        RadiusKm = radiusKm;
    }

    /// <summary>Breitengrad der Mitte.</summary>
    public double Breite { get; }

    /// <summary>Längengrad der Mitte.</summary>
    public double Laenge { get; }

    /// <summary>Der Radius in Kilometern.</summary>
    public double RadiusKm { get; }

    /// <summary>Baut einen Umkreis aus dem, was in der Abfrage stand.</summary>
    /// <remarks>
    /// Unvollständiges oder Unsinniges ergibt <c>null</c> — also KEINEN Filter,
    /// und keinen Fehler. Dieselbe Haltung wie bei der Seitenwahl: eine Liste,
    /// die auf <c>?lat=abc</c> mit 400 antwortet, ist an einer Stelle streng, an
    /// der niemand etwas gewinnt. Der Radius wird geklemmt statt verworfen —
    /// wer 100000 schickt, meint „weit", und daraus keinen Filter zu machen
    /// wäre die überraschendere Antwort.
    /// </remarks>
    /// <param name="breite">Breitengrad, oder <c>null</c>.</param>
    /// <param name="laenge">Längengrad, oder <c>null</c>.</param>
    /// <param name="radiusKm">Der gewünschte Radius, oder <c>null</c>.</param>
    /// <returns>Der Umkreis, oder <c>null</c>, wenn keiner gemeint war.</returns>
    public static Umkreis? Aus(double? breite, double? laenge, double? radiusKm)
    {
        if (breite is not { } b || laenge is not { } l || radiusKm is not { } r)
        {
            return null;
        }

        if (double.IsNaN(b) || double.IsNaN(l) || double.IsNaN(r)
            || b is < -90 or > 90 || l is < -180 or > 180)
        {
            return null;
        }

        return new Umkreis(b, l, Math.Clamp(r, KleinsterRadiusKm, GroessterRadiusKm));
    }
}
