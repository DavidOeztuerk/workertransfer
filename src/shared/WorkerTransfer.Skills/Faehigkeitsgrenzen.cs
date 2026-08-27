namespace WorkerTransfer.Skills;

/// <summary>Die eine Zahl, über die zwei Dienste sich einig sein müssen.</summary>
/// <remarks>
/// <c>profile-service</c> und <c>jobs-service</c> führen jeweils ihre eigene
/// Fähigkeitenliste — so will es die Sharing-Regel, denn wie viele Einträge
/// eine Liste trägt, ist eine Entscheidung des Dienstes, dem sie gehört. Ein
/// Profil hält dreißig, eine Stelle zwanzig, und das ist in Ordnung.
/// <para>
/// <b>Die Länge ist es nicht.</b> Der Abgleich findet im Browser statt: dürfte
/// eine Ausschreibung eine Anforderung nennen, die länger ist, als eine Person
/// sie überhaupt eintragen kann, wäre sie garantiert nie ein Treffer — eine
/// Zeile in der Liste, die für niemanden je ein Haken werden kann.
/// </para>
/// <para>
/// Der Python-Dienst hielt das mit einem Test zusammen, der beide Konstanten
/// importierte. Hier ist es <em>eine</em> Konstante: die zwei können nicht mehr
/// auseinanderlaufen, weil es nichts gibt, was auseinanderlaufen könnte.
/// </para>
/// </remarks>
public static class Faehigkeitsgrenzen
{
    /// <summary>Wie lang eine einzelne Fähigkeit sein darf.</summary>
    public const int Hoechstlaenge = 50;
}
