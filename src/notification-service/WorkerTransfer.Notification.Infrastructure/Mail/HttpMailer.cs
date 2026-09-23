using System.Net.Http.Json;
using Noelia.Core.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Notification.Application.Ports;

namespace WorkerTransfer.Notification.Infrastructure.Mail;

/// <summary>Where identity-service answers, and what identifies us as us.</summary>
/// <remarks>
/// The property names ARE the environment keys (<c>Identity__Adresse</c>,
/// <c>Identity__Geheimnis</c>, <c>Identity__Zeitueberschreitung</c>), so they
/// stay German until the keys are renamed everywhere at once — in compose, in
/// the chart and in <c>.env.example</c>.
/// </remarks>
public sealed class MailSettings
{
    /// <summary>The configuration section this binds to.</summary>
    public const string Section = "Identity";

    /// <summary>The base address of identity-service.</summary>
    public string Adresse { get; set; } = string.Empty;

    /// <summary>The shared secret. Expressly not the erasure's.</summary>
    public string Geheimnis { get; set; } = string.Empty;

    /// <summary>How long a call may take.</summary>
    public TimeSpan Zeitueberschreitung { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>Asks identity-service to send the one sentence out.</summary>
/// <remarks>
/// <strong>Fire and forget.</strong> A failure here must never topple the case
/// that triggered it — the exact inversion of the consent rule, and right for
/// the same reason: with the ledger it is about permission (in doubt, no), here
/// about courtesy. Rolling back a revocation because a mail did not go out
/// would be grotesque.
/// <para>
/// The body carries <strong>only the id</strong>. Neither the kind nor a text:
/// identity-service phrases the one fixed sentence, and a caller that could
/// contribute something would be the beginning of "just this one line as
/// well".
/// </para>
/// </remarks>
public sealed class HttpMailer(
    IHttpClientFactory factory,
    IOptions<MailSettings> settings,
    ILogger<HttpMailer> log) : IMailer
{
    /// <summary>The name the client is registered under.</summary>
    public const string ClientName = "identity";

    private readonly MailSettings _settings = settings.Value;

    /// <inheritdoc />
    public async Task SendAsync(SubjectId subject, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_settings.Adresse)
            || string.IsNullOrEmpty(_settings.Geheimnis))
        {
            // No secret, no attempt: the endpoint would be shut anyway, and a
            // call into the void costs a timeout at every case.
            return;
        }

        try
        {
            using var client = factory.CreateClient(ClientName);
            client.Timeout = _settings.Zeitueberschreitung;

            using var request = new HttpRequestMessage(
                HttpMethod.Post, new Uri(new Uri(_settings.Adresse), "/internal/notify"))
            {
                // `user_id`, not `userId`. identity-service declares
                // [JsonPropertyName("user_id")]; camelCase bound silently to
                // Guid.Empty. This failure is the quietest in the system — it
                // does not throw, it logs a warning, and the outbox never sees
                // it. Measured: the inbox held nothing but the confirmation
                // mails identity sends itself.
                Content = JsonContent.Create(new { user_id = subject.Value })
            };

            request.Headers.Add("X-Notify-Secret", _settings.Geheimnis);

            using var response = await client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // What is logged is the kind of failure, never the recipient:
                // a log is also a place where something ends up.
                log.LogWarning(
                    "identity-service answered with {Code}", (int)response.StatusCode);
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // Swallows every error. That is the purpose, not carelessness —
            // the throttle is already set, this message is lost, and that is
            // the right direction.
            log.LogWarning(
                "The notification could not be triggered ({Kind})", error.GetType().Name);
        }
    }
}
