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
    IHttpClientFactory factory,
    IErasureLedger erasureLedger,
    IMailTray mailTray,
    IMailTrayDispatch dispatch,
    IOptions<ErasureSettings> settings,
    TimeProvider clock) : IDelivery
{
    private readonly ErasureSettings _settings = settings.Value;

    /// <inheritdoc />
    public async Task DeliverAsync(
        SubjectId recipient,
        string kind,
        CancellationToken cancellationToken = default)
    {
        if (kind == ErasureRecipient.CompanyWithdrawal)
        {
            await WithdrawAdverts(recipient, cancellationToken);
            return;
        }

        if (kind == ErasureRecipient.FinalNotice)
        {
            await SendClosingNotice(recipient, cancellationToken);
            return;
        }

        if (kind == ErasureRecipient.Identity)
        {
            await Finish(recipient, cancellationToken);
            return;
        }

        var service = kind[ErasureRecipient.Prefix.Length..];
        await PostAsync(service, "/erasure", new ErasureV1(recipient.Value), cancellationToken);
    }

    /// <summary>
    /// Every recipient has to have acknowledged.
    /// </summary>
    /// <remarks>
    /// A dead recipient therefore <em>blocks</em> the notice, and that is the
    /// point: a message saying "everything is gone" while a service still holds
    /// data would be the one lie this whole cascade exists to prevent.
    /// </remarks>
    private async Task SendClosingNotice(
        SubjectId subject,
        CancellationToken cancellationToken)
    {
        var open = await erasureLedger.OpenIntentsAsync(subject, cancellationToken);

        if (open.Any(kind => kind != ErasureRecipient.FinalNotice
                             && kind != ErasureRecipient.Identity))
        {
            throw new NotYetException("not every recipient has acknowledged yet");
        }

        // Read now, not carried along: the address and the language live in the
        // row that is about to be deleted, and putting either in the outbox
        // would write it into every backup (ADR-0025 §5).
        var address = await erasureLedger.AddressAsync(subject, cancellationToken);

        if (address is not null)
        {
            mailTray.ErasureConfirmation(address.Address, subject, address.Language);
            await dispatch.SendCollectedAsync(cancellationToken);
        }
    }

    /// <summary>The account falls last, and only after the notice went out.</summary>
    private async Task Finish(SubjectId subject, CancellationToken cancellationToken)
    {
        var open = await erasureLedger.OpenIntentsAsync(subject, cancellationToken);

        if (open.Any(kind => kind != ErasureRecipient.Identity))
        {
            throw new NotYetException("the closing notice is still outstanding");
        }

        var dormant = await erasureLedger.CompleteAsync(subject, clock.GetUtcNow(), cancellationToken);

        foreach (var company in dormant)
        {
            await PostAsync(
                "jobs", "/companies/withdrawal",
                new CompanyWithdrawalV1(company.Value), cancellationToken);
        }
    }

    private Task WithdrawAdverts(SubjectId company, CancellationToken cancellationToken) =>
        PostAsync("jobs", "/companies/withdrawal",
            new CompanyWithdrawalV1(company.Value), cancellationToken);

    /// <summary>
    /// One call, and it throws on anything that is not a success.
    /// </summary>
    /// <remarks>
    /// Deliberately unlike an ordinary notification, which swallows both a
    /// transport error and a bad status — right for a mail, and here it would
    /// make <c>delivered_at</c> a lie.
    /// </remarks>
    private async Task PostAsync<T>(
        string service,
        string path,
        T body,
        CancellationToken cancellationToken)
    {
        if (!_settings.BaseUrls.TryGetValue(service, out var baseUrl))
        {
            throw new InvalidOperationException($"No address configured for '{service}'.");
        }

        // Empty means nothing is delivered — and as a FAILURE, not as a silent
        // skip. A row that counted as delivered while nobody deleted anything
        // would be the worst case there is.
        if (string.IsNullOrEmpty(_settings.Secret))
        {
            throw new InvalidOperationException("Erasure:Secret is not set.");
        }

        using var client = factory.CreateClient(nameof(HttpErasureDelivery));

        client.Timeout = _settings.Timeout;
        using var request = new HttpRequestMessage(
            HttpMethod.Post, new Uri(new Uri(baseUrl), path))
        {
            Content = JsonContent.Create(body)
        };

        request.Headers.Add("X-Erasure-Secret", _settings.Secret);

        using var response = await client.SendAsync(request, cancellationToken);

        response.EnsureSuccessStatusCode();
    }
}
