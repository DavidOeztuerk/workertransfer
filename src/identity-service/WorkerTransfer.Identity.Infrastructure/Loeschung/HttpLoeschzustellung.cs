using System.Net.Http.Json;
using Noelia.Core.Identity;
using Microsoft.Extensions.Options;
using WorkerTransfer.Contracts.Erasure;
using WorkerTransfer.Identity.Application.Erasure;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Outbox;

namespace WorkerTransfer.Identity.Infrastructure.Erasure;

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
public sealed class HttpErasureDelivery(
    IHttpClientFactory fabrik,
    IErasureLedger bestand,
    IPostkorb postkorb,
    IPostkorbVersand versand,
    IOptions<ErasureSettings> einstellungen,
    TimeProvider uhr) : IDelivery
{
    private readonly ErasureSettings _einstellungen = einstellungen.Value;

    /// <inheritdoc />
    public async Task DeliverAsync(
        SubjectId recipient,
        string kind,
        CancellationToken cancellationToken = default)
    {
        if (kind == ErasureRecipient.Unternehmensrueckzug)
        {
            await ZiehAnzeigenZurueck(recipient, cancellationToken);
            return;
        }

        if (kind == ErasureRecipient.Schlussnachricht)
        {
            await SchickeSchlussnachricht(recipient, cancellationToken);
            return;
        }

        if (kind == ErasureRecipient.Identitaet)
        {
            await SchliesseAb(recipient, cancellationToken);
            return;
        }

        var service = kind[ErasureRecipient.Praefix.Length..];
        await Frag(service, "/erasure", new ErasureV1(recipient.Value), cancellationToken);
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
        SubjectId subject,
        CancellationToken cancellationToken)
    {
        var offen = await bestand.OpenIntentsAsync(subject, cancellationToken);

        if (offen.Any(kind => kind != ErasureRecipient.Schlussnachricht
                             && kind != ErasureRecipient.Identitaet))
        {
            throw new NochNichtException("noch nicht alle Empfänger haben quittiert");
        }

        // Read now, not carried along: the address and the language live in the
        // row that is about to be deleted, and putting either in the outbox
        // would write it into every backup (ADR-0025 §5).
        var anschrift = await bestand.AddressAsync(subject, cancellationToken);

        if (anschrift is not null)
        {
            postkorb.Loeschbestaetigung(anschrift.Adresse, subject, anschrift.Sprache);
            await versand.SendCollectedAsync(cancellationToken);
        }
    }

    /// <summary>The account falls last, and only after the notice went out.</summary>
    private async Task SchliesseAb(SubjectId subject, CancellationToken cancellationToken)
    {
        var offen = await bestand.OpenIntentsAsync(subject, cancellationToken);

        if (offen.Any(kind => kind != ErasureRecipient.Identitaet))
        {
            throw new NochNichtException("die Schlussnachricht steht noch aus");
        }

        var stillgelegt = await bestand.CompleteAsync(subject, uhr.GetUtcNow(), cancellationToken);

        foreach (var company in stillgelegt)
        {
            await Frag(
                "jobs", "/companies/withdrawal",
                new UnternehmensrueckzugV1(company.Value), cancellationToken);
        }
    }

    private Task ZiehAnzeigenZurueck(SubjectId company, CancellationToken cancellationToken) =>
        Frag("jobs", "/companies/withdrawal",
            new UnternehmensrueckzugV1(company.Value), cancellationToken);

    /// <summary>
    /// One call, and it throws on anything that is not a success.
    /// </summary>
    /// <remarks>
    /// Deliberately unlike an ordinary notification, which swallows both a
    /// transport error and a bad status — right for a mail, and here it would
    /// make <c>delivered_at</c> a lie.
    /// </remarks>
    private async Task Frag<T>(
        string service,
        string path,
        T body,
        CancellationToken cancellationToken)
    {
        if (!_einstellungen.Adressen.TryGetValue(service, out var basis))
        {
            throw new InvalidOperationException($"Keine Adresse für '{service}' konfiguriert.");
        }

        // Empty means nothing is delivered — and as a FAILURE, not as a silent
        // skip. A row that counted as delivered while nobody deleted anything
        // would be the worst case there is.
        if (string.IsNullOrEmpty(_einstellungen.Geheimnis))
        {
            throw new InvalidOperationException("Erasure:Geheimnis ist nicht gesetzt.");
        }

        using var client = fabrik.CreateClient(nameof(HttpErasureDelivery));

        client.Timeout = _einstellungen.Zeitueberschreitung;
        using var request = new HttpRequestMessage(
            HttpMethod.Post, new Uri(new Uri(basis), path))
        {
            Content = JsonContent.Create(body)
        };

        request.Headers.Add("X-Erasure-Secret", _einstellungen.Geheimnis);

        using var response = await client.SendAsync(request, cancellationToken);

        response.EnsureSuccessStatusCode();
    }
}
