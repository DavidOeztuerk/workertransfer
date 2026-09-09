using System.Net.Http.Json;
using Girder.Core.Identity;
using Microsoft.Extensions.Options;
using WorkerTransfer.Contracts.Erasure;
using WorkerTransfer.Identity.Application.Loeschung;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Outbox;

namespace WorkerTransfer.Identity.Infrastructure.Loeschung;

/// <summary>Delivers the cascade, and enforces its order.</summary>
/// <remarks>
/// Three kinds of row, three behaviours:
/// <list type="bullet">
/// <item>a recipient — an HTTP call that <em>must be able to fail</em>;</item>
/// <item>the final notice — deferred until every recipient has acknowledged;</item>
/// <item>identity itself — deferred until the notice went out, and only then do
/// the rows fall.</item>
/// </list>
/// The order is enforced here rather than advised in a comment, because a
/// comment cannot stop the account from falling while somebody still holds
/// data (ADR-0027 §6).
/// </remarks>
public sealed class HttpLoeschzustellung(
    IHttpClientFactory fabrik,
    ILoeschbestand bestand,
    IPostkorb postkorb,
    IPostkorbVersand versand,
    IOptions<Loescheinstellungen> einstellungen,
    TimeProvider uhr) : IZustellung
{
    private readonly Loescheinstellungen _einstellungen = einstellungen.Value;

    /// <inheritdoc />
    public async Task ZustelleAsync(
        SubjectId empfaenger,
        string art,
        CancellationToken cancellationToken = default)
    {
        if (art == Loeschempfaenger.Unternehmensrueckzug)
        {
            await ZiehAnzeigenZurueck(empfaenger, cancellationToken);
            return;
        }

        if (art == Loeschempfaenger.Schlussnachricht)
        {
            await SchickeSchlussnachricht(empfaenger, cancellationToken);
            return;
        }

        if (art == Loeschempfaenger.Identitaet)
        {
            await SchliesseAb(empfaenger, cancellationToken);
            return;
        }

        var dienst = art[Loeschempfaenger.Praefix.Length..];
        await Frag(dienst, "/erasure", new LoeschungV1(empfaenger.Value), cancellationToken);
    }

    /// <summary>
    /// Every recipient has to have acknowledged.
    /// </summary>
    /// <remarks>
    /// A dead recipient therefore <em>blocks</em> the notice, and that is the
    /// point: a message saying "everything is gone" while a service still holds
    /// data would be the one lie this whole cascade exists to prevent.
    /// </remarks>
    private async Task SchickeSchlussnachricht(
        SubjectId wer,
        CancellationToken cancellationToken)
    {
        var offen = await bestand.OffeneAbsichtenAsync(wer, cancellationToken);

        if (offen.Any(art => art != Loeschempfaenger.Schlussnachricht
                             && art != Loeschempfaenger.Identitaet))
        {
            throw new NochNichtException("noch nicht alle Empfänger haben quittiert");
        }

        // Read now, not carried along: the address and the language live in the
        // row that is about to be deleted, and putting either in the outbox
        // would write it into every backup (ADR-0025 §5).
        var anschrift = await bestand.AdresseAsync(wer, cancellationToken);

        if (anschrift is not null)
        {
            postkorb.Loeschbestaetigung(anschrift.Adresse, wer, anschrift.Sprache);
            await versand.VersendeGesammeltesAsync(cancellationToken);
        }
    }

    /// <summary>The account falls last, and only after the notice went out.</summary>
    private async Task SchliesseAb(SubjectId wer, CancellationToken cancellationToken)
    {
        var offen = await bestand.OffeneAbsichtenAsync(wer, cancellationToken);

        if (offen.Any(art => art != Loeschempfaenger.Identitaet))
        {
            throw new NochNichtException("die Schlussnachricht steht noch aus");
        }

        var stillgelegt = await bestand.SchliesseAbAsync(wer, uhr.GetUtcNow(), cancellationToken);

        foreach (var firma in stillgelegt)
        {
            await Frag(
                "jobs", "/companies/withdrawal",
                new UnternehmensrueckzugV1(firma.Value), cancellationToken);
        }
    }

    private Task ZiehAnzeigenZurueck(SubjectId firma, CancellationToken cancellationToken) =>
        Frag("jobs", "/companies/withdrawal",
            new UnternehmensrueckzugV1(firma.Value), cancellationToken);

    /// <summary>
    /// One call, and it throws on anything that is not a success.
    /// </summary>
    /// <remarks>
    /// Deliberately unlike an ordinary notification, which swallows both a
    /// transport error and a bad status — right for a mail, and here it would
    /// make <c>delivered_at</c> a lie.
    /// </remarks>
    private async Task Frag<T>(
        string dienst,
        string pfad,
        T rumpf,
        CancellationToken cancellationToken)
    {
        if (!_einstellungen.Adressen.TryGetValue(dienst, out var basis))
        {
            throw new InvalidOperationException($"Keine Adresse für '{dienst}' konfiguriert.");
        }

        // Empty means nothing is delivered — and as a FAILURE, not as a silent
        // skip. A row that counted as delivered while nobody deleted anything
        // would be the worst case there is.
        if (string.IsNullOrEmpty(_einstellungen.Geheimnis))
        {
            throw new InvalidOperationException("Erasure:Geheimnis ist nicht gesetzt.");
        }

        using var client = fabrik.CreateClient(nameof(HttpLoeschzustellung));

        client.Timeout = _einstellungen.Zeitueberschreitung;
        using var anfrage = new HttpRequestMessage(
            HttpMethod.Post, new Uri(new Uri(basis), pfad))
        {
            Content = JsonContent.Create(rumpf)
        };

        anfrage.Headers.Add("X-Erasure-Secret", _einstellungen.Geheimnis);

        using var antwort = await client.SendAsync(anfrage, cancellationToken);

        antwort.EnsureSuccessStatusCode();
    }
}
