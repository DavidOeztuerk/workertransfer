namespace WorkerTransfer.GitHub.Application.Ports;

/// <summary>Der Nachweis über GitHubs eigene Anmeldung.</summary>
/// <remarks>
/// <strong>Er ersetzt den Gist, nicht die Nennung.</strong> Die Person sagt
/// weiterhin zuerst, welches Konto ihr gehört; OAuth beantwortet danach die
/// Frage, ob sie darüber verfügt. Anders herum ginge es auch — GitHub nennt uns
/// den Namen ja —, aber dann wäre der Ablauf für beide Wege verschieden, und
/// der Gist-Weg bleibt bestehen, solange keine Zugangsdaten hinterlegt sind.
///
/// <para>
/// <strong>Das Zugriffstoken wird nicht behalten.</strong> Gebraucht wird es
/// für genau einen Aufruf — <c>GET /user</c>, um den Anmeldenamen zu erfahren.
/// Danach ist es wertlos für uns und wäre nur noch ein fremdes Geheimnis in
/// unserer Datenbank. Was man nicht aufhebt, kann man nicht verlieren.
/// </para>
///
/// <para>
/// Deshalb auch <strong>kein Geltungsbereich</strong>: <c>GET /user</c>
/// beantwortet die Frage ohne jeden <c>scope</c>. Ein <c>repo</c>-Recht zu
/// erbitten, um öffentliche Repositories zu lesen, wäre eine Vollmacht für
/// etwas, das ohnehin öffentlich ist.
/// </para>
/// </remarks>
public interface IGitHubAnmeldung
{
    /// <summary>Ist eine Anmeldung über GitHub überhaupt eingerichtet?</summary>
    /// <remarks>
    /// Ohne Zugangsdaten bleibt der Gist der Weg. Die Oberfläche fragt das, um
    /// zu wissen, welchen Weg sie anbietet — und nicht, um einen Knopf zu
    /// zeigen, der dann nicht funktioniert.
    /// </remarks>
    bool Eingerichtet { get; }

    /// <summary>Wohin der Browser geschickt wird.</summary>
    /// <param name="zustand">
    /// Die Einmalzeichenfolge der Verbindung. GitHub reicht sie unverändert
    /// zurück; sie beweist, dass die Antwort zu DIESER Anfrage gehört und nicht
    /// zu einer, die jemand anderes untergeschoben hat.
    /// </param>
    Uri Anmeldeadresse(string zustand);

    /// <summary>Tauscht den Einmalcode gegen den Anmeldenamen.</summary>
    /// <remarks>
    /// Gibt <c>null</c> zurück, wenn GitHub den Code nicht annimmt — das ist
    /// eine Aussage über den Code, kein Ausfall.
    /// </remarks>
    /// <exception cref="GitHubSchweigt">GitHub hat nicht geantwortet.</exception>
    Task<string?> AnmeldenamenAsync(string code, CancellationToken cancellationToken = default);
}
