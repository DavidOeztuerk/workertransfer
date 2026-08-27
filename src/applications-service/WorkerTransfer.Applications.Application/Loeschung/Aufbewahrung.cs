namespace WorkerTransfer.Applications.Application.Loeschung;

/// <summary>Der Schalter, der auf AUS steht (ADR-0027 §3).</summary>
/// <remarks>
/// <strong>Die Voreinstellung löscht vollständig, auch <c>status = 'hired'</c>.</strong>
/// Das ist die tragende Entscheidung dieses Dienstes und eine Entscheidung über
/// die <em>Beweislast</em>: nicht die Löschung muss sich rechtfertigen, sondern
/// das Behalten.
/// <para>
/// Die Einschätzung, die sie trägt (Auftraggeber, 06.08.2026, ausdrücklich als
/// Einschätzung und nicht als Rechtsrat): die Plattform ist nicht der
/// Arbeitgeber. Aufbewahrungspflichten für Arbeitsverträge treffen das
/// Unternehmen mit <em>seinen eigenen</em> Unterlagen, nicht einen Vermittler.
/// Für Bewerbungsdaten gilt eher das Gegenteil einer Aufbewahrungspflicht: eine
/// kurze Karenz wegen der AGG-Klagefrist, danach ist zu löschen. Offen ist genau
/// ein anwaltlich zu bestätigender Satz; er ändert nicht diesen Entwurf, sondern
/// nur die Stellung des Schalters — und blockiert deshalb nichts.
/// </para>
/// <para>
/// <strong>Was das für das Unternehmen heißt, offen gesagt:</strong> löscht ein
/// Mensch sein Konto, verschwindet auch die Bewerbung, über die er eingestellt
/// wurde, aus der Liste des Unternehmens. Das ist gewollt, und
/// <c>/delete-account</c> sagt es vor dem Knopf.
/// </para>
/// </remarks>
public static class Aufbewahrung
{
    /// <summary><strong>AUS.</strong> Eingestellte Bewerbungen fallen mit.</summary>
    /// <remarks>
    /// Eine benannte Konstante und <strong>kein Konfigurationswert</strong>: bei
    /// einem Löschversprechen wäre „in Produktion anders als im Test" der
    /// schlimmste denkbare Zustand. Sie umzulegen ist ein sichtbarer Commit, den
    /// jemand begründen muss — nicht eine Umgebungsvariable, die niemand liest.
    /// <para>
    /// Sie schaltet <strong>genau eine Zeilenklasse in diesem Dienst</strong>:
    /// <c>status = 'hired'</c>. Keine Ausdehnung auf <c>rejected</c> — eine
    /// abgelehnte Bewerbung begründet nichts — und kein „laufender Vorgang" als
    /// Gummiwort. Und keine Frist, in keiner Richtung: weder eine geratene Dauer
    /// noch ein Nachlauf, der später aufräumt. Wird sie je umgelegt, kommt die
    /// Frist <em>zusammen mit der Antwort</em>, nicht vorher.
    /// </para>
    /// <para>
    /// Der <see cref="Ports.ILoeschbestand"/> nimmt sie als Parameter entgegen,
    /// statt sie selbst zu lesen. Sonst ließe sich nur prüfen, dass sie auf aus
    /// steht — nicht, was der umgelegte Schalter abdeckt, und genau das ist die
    /// Aussage, auf die es ankommt.
    /// </para>
    /// <para>
    /// <c>static readonly</c> und nicht <c>const</c>, aus einem gemessenen
    /// Grund: ein <c>const</c> wird in jede lesende Assembly hineinkopiert.
    /// Bei einer Gegenprobe stand er hier wieder auf <c>false</c>, während die
    /// Testassembly noch das alte <c>true</c> trug — ein Test, der über
    /// unverändertem Code fällt, ist schlimmer als gar keiner. Ein Feld wird
    /// gelesen, wo es steht.
    /// </para>
    /// </remarks>
    public static readonly bool EingestellteBehalten = false;
}
