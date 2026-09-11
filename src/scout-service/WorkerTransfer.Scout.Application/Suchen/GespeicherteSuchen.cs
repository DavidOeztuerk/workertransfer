using Girder.Core.Identity;
using MediatR;
using WorkerTransfer.Scout.Application.Nachrichten;
using WorkerTransfer.Scout.Domain.Suchen;

namespace WorkerTransfer.Scout.Application.Suchen;

/// <summary>Legt eine Anfrage ab — nie ein Ergebnis.</summary>
public sealed record SucheSpeichernBefehl(
    TenantId Firma,
    SubjectId Wer,
    string Name,
    Suchfilter Filter) : IBefehl<Suche>;

/// <summary>Speichert die Filter und sonst nichts.</summary>
/// <remarks>
/// <para><strong>ADR-0036 Entscheidung 4 in einem Handler.</strong> Was hier
/// entsteht, ist die Frage: Worte, Ort, Remote. Kein Treffer, keine Kennung
/// eines gefundenen Menschen, kein Zeitpunkt eines Laufs. Ein gespeichertes
/// Ergebnis über Menschen veraltet gegen einen Widerruf — und ein Widerruf muss
/// beim nächsten Aufruf wirken, nicht beim übernächsten (ADR-0013).</para>
///
/// <para>Eine Obergrenze je Mensch, damit das Ablegen nicht zu einem Speicher
/// wird, den niemand aufräumt.</para>
/// </remarks>
public sealed class SucheSpeichernHandler(ISuchspeicher speicher, TimeProvider uhr)
    : IRequestHandler<SucheSpeichernBefehl, Suche>
{
    /// <inheritdoc />
    /// <exception cref="Eingabefehler">Der Name fehlt, ist zu lang, oder es sind zu viele.</exception>
    public async Task<Suche> Handle(
        SucheSpeichernBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var bisher = await speicher.FuerMenschAsync(
            request.Firma, request.Wer, cancellationToken);

        if (bisher.Count >= Suche.HoechstzahlJeMensch)
        {
            throw new Eingabefehler(
                $"at most {Suche.HoechstzahlJeMensch} saved searches per person and company");
        }

        var suche = Suche.Lege_an(
            request.Firma, request.Wer, request.Name, request.Filter, uhr.GetUtcNow());

        await speicher.FuegeHinzuAsync(suche, cancellationToken);

        return suche;
    }
}

/// <summary>Die eigenen gespeicherten Suchen in diesem Unternehmen.</summary>
public sealed record MeineSuchenAbfrage(TenantId Firma, SubjectId Wer)
    : IAbfrage<IReadOnlyList<Suche>>;

/// <summary>Liest, was dieser Mensch für dieses Unternehmen abgelegt hat.</summary>
public sealed class MeineSuchenHandler(ISuchspeicher speicher)
    : IRequestHandler<MeineSuchenAbfrage, IReadOnlyList<Suche>>
{
    /// <inheritdoc />
    public Task<IReadOnlyList<Suche>> Handle(
        MeineSuchenAbfrage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return speicher.FuerMenschAsync(request.Firma, request.Wer, cancellationToken);
    }
}

/// <summary>Nimmt eine gespeicherte Suche wieder weg.</summary>
public sealed record SucheLoeschenBefehl(Guid Id, TenantId Firma, SubjectId Wer) : IBefehl<bool>;

/// <summary>Entfernt sie, wenn sie diesem Menschen in diesem Unternehmen gehört.</summary>
/// <remarks>
/// Beide Bedingungen im Speicher und nicht hier: ein Handler, der erst liest
/// und dann vergleicht, hat zwischen den beiden Schritten eine Lücke — und ein
/// Treffer, der „gehört dir nicht" von „gibt es nicht" unterscheidet, verriete,
/// dass die Suche eines Kollegen existiert.
/// </remarks>
public sealed class SucheLoeschenHandler(ISuchspeicher speicher)
    : IRequestHandler<SucheLoeschenBefehl, bool>
{
    /// <inheritdoc />
    public Task<bool> Handle(SucheLoeschenBefehl request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return speicher.EntferneAsync(
            request.Id, request.Firma, request.Wer, cancellationToken);
    }
}
