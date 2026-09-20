namespace WorkerTransfer.Nachweis.Pruefungen;

/// <summary>Der Anbieter, den der Betreiber in der Umgebung eingerichtet hat.</summary>
/// <remarks>
/// <para>Die eine Hälfte des Verzeichnisses. Die andere steht in
/// identity-service, wo jede Person ihren eigenen Zugang einträgt — dort wird
/// gezählt, hier nicht: dieser Zugang hängt an keiner Person und gilt für jede.</para>
///
/// <para><strong>Der Schlüssel kommt hier nicht vor, und die Adresse auch
/// nicht.</strong> Übrig bleibt der Host, und der ist der Gegenstand des
/// Verzeichnisses: ein Empfänger im Sinne von Art. 30 Abs. 1 DSGVO wird über
/// seinen Namen benannt, nicht über den Endpunkt, an dem man ihn ruft.</para>
///
/// <para><strong>Nicht eingerichtet heißt: keine Zeile.</strong> Eine Zeile
/// „anthropic, nicht in Gebrauch" wäre ein Eintrag über einen Empfänger, den es
/// nicht gibt — und der Vorgabewert der Adresse stünde dann in einem Dokument,
/// als spräche jemand mit ihm.</para>
/// </remarks>
/// <param name="etikett">Das Anbieter-Etikett.</param>
/// <param name="adresse">Die Adresse aus der Konfiguration.</param>
/// <param name="eingerichtet">Ob überhaupt jemand gerufen wird.</param>
public sealed class Betreiberquelle(string etikett, string adresse, bool eingerichtet)
    : IAnbieterquelle
{
    /// <inheritdoc />
    public Task<IReadOnlyList<Anbieterzeile>> LeseAsync(CancellationToken ct = default)
    {
        if (!eingerichtet
            || !Uri.TryCreate(adresse, UriKind.Absolute, out var ziel)
            || ziel.Host.Length == 0)
        {
            return Task.FromResult<IReadOnlyList<Anbieterzeile>>([]);
        }

        return Task.FromResult<IReadOnlyList<Anbieterzeile>>(
            [new Anbieterzeile(etikett, ziel.Host, Herkunft.Betreiber, 0)]);
    }
}
