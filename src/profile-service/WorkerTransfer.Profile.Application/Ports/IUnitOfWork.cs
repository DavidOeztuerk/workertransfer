namespace WorkerTransfer.Profile.Application.Ports;

/// <summary>Eine Transaktion um einen Befehl.</summary>
/// <remarks>
/// Girder bringt keine Transaktionsklammer mit, und der Grund, warum dieser
/// Dienst eine braucht, ist die Prüfspur: ein Eintrag, der getrennt abschließt
/// von der Änderung, die er festhält, überlebt einen Rücklauf oder fehlt nach
/// einem Abschluss — und behauptet in beiden Fällen etwas, das nicht geschehen
/// ist. Für die Löschung gilt es härter: die Zeile fällt und die Spur vermerkt
/// es, oder es geschieht nichts.
/// <para>
/// Wiedereintrittsfähig laut Vertrag: ein verschachtelter Aufruf tritt der
/// offenen Transaktion bei, statt eine zweite zu öffnen.
/// </para>
/// </remarks>
public interface IUnitOfWork
{
    /// <param name="arbeit">Was innerhalb der Transaktion laufen soll.</param>
    /// <param name="cancellationToken">Bricht ab und läuft zurück.</param>
    /// <returns>Was <paramref name="arbeit"/> geantwortet hat.</returns>
    Task<T> InEinerTransaktionAsync<T>(
        Func<CancellationToken, Task<T>> arbeit,
        CancellationToken cancellationToken = default);
}
