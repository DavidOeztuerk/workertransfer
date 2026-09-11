namespace WorkerTransfer.ServiceDefaults.Rollen;

/// <summary>Wer welche Rolle in welchem Unternehmen hat.</summary>
/// <remarks>
/// <para>Ein Port, kein Dienst: die Antwort kommt heute über den internen Draht
/// von identity-service, und in einer Testreihe aus einer Attrappe. Ohne ihn
/// müsste jede Reihe, die einen Firmenendpunkt anfasst, einen zweiten Dienst
/// mitstarten.</para>
/// </remarks>
public interface IFirmenrollen
{
    /// <summary>Die Rolle, oder <see cref="Firmenrolle.Keine"/>.</summary>
    /// <param name="wer">Wer fragt.</param>
    /// <param name="firma">Für welches Unternehmen.</param>
    /// <param name="cancellationToken">Bricht die Abfrage ab.</param>
    /// <exception cref="RolleSchweigt">
    /// Die Auskunft war nicht zu bekommen. <strong>Nicht dasselbe wie
    /// <see cref="Firmenrolle.Keine"/>:</strong> „wir wissen es nicht" als
    /// „du darfst nicht" zu beantworten wäre eine Lüge, und zwar eine, die nach
    /// einer Entscheidung aussieht.
    /// </exception>
    Task<Firmenrolle> RolleAsync(
        Guid wer,
        Guid firma,
        CancellationToken cancellationToken = default);
}

/// <summary>Die Rollenauskunft hat nicht geantwortet.</summary>
/// <remarks>
/// Führt zu 503 und nie zu 403 — dieselbe Unterscheidung, die dieser Baum schon
/// beim Einwilligungs-Ledger trifft: 503 heisst „hat nicht geantwortet", nicht
/// „nein".
/// </remarks>
public sealed class RolleSchweigt : Exception
{
    /// <summary>Mit einem Grund, der keine Werte nennt.</summary>
    /// <param name="message">Was fehlte — eine Gestalt, nie ein Wert.</param>
    public RolleSchweigt(string message) : base(message)
    {
    }

    /// <summary>Mit einem Grund und der Ursache darunter.</summary>
    /// <param name="message">Was fehlte.</param>
    /// <param name="innerException">Was darunter schiefging.</param>
    public RolleSchweigt(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Parameterlos, weil die Analyse es verlangt.</summary>
    public RolleSchweigt()
    {
    }
}
