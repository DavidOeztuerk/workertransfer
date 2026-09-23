using System.Net;
using System.Text.Json;
using FluentAssertions;
using Noelia.Core.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WorkerTransfer.Notification.Contracts;
using WorkerTransfer.Notification.Infrastructure.Mail;
using AnwendungenPost = WorkerTransfer.Applications.Infrastructure.Benachrichtigung;
using LebenslaufPost = WorkerTransfer.Resume.Infrastructure.Benachrichtigung;
using SpaeherPost = WorkerTransfer.Scout.Infrastructure.Benachrichtigung;
using WechselPost = WorkerTransfer.Transfer.Infrastructure.Notification;

namespace WorkerTransfer.Ganzes.Tests;

/// <summary>
/// What three services send to <c>notification-service</c>, that service has to
/// be able to read.
/// </summary>
/// <remarks>
/// <para><strong>The occasion.</strong> All three sent <c>userId</c>, the
/// receiver declares <c>[JsonPropertyName("user_id")]</c>. The id bound silently
/// to <c>Guid.Empty</c>, the service answered <b>500</b>, and because the
/// dispatch has an <c>EnsureSuccessStatusCode()</c>, the outbox gave up after
/// ten attempts. Measured against the running stack: 18 rows, not one of them
/// delivered, in the whole inbox nothing but confirmation mails. <b>The
/// notification path never worked.</b></para>
///
/// <para><strong>Why this test and not a comparison of names.</strong> A test
/// that asks "is the field called user_id?" checks the same string twice and
/// breaks together with it. Here the body the sender REALLY writes is captured
/// instead, and deserialised into the receiver's contract. Green then means: the
/// id arrives. That is the promise, and it survives every rename.</para>
///
/// <para><strong>Why no other suite catches it.</strong>
/// <c>DrahtvertragTests</c> reads declared properties in the API and contracts
/// assemblies. Here the body stands as an <em>anonymous object</em> in the
/// infrastructure — no declared type, so nothing to check. And no service suite
/// crosses the seam: in the tests the sender has no receiver, the receiver no
/// sender.</para>
/// </remarks>
public class BenachrichtigungsdrahtTests
{
    private static readonly Guid Empfaenger = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301");

    /// <summary>The body of transfer-service.</summary>
    [Fact]
    public Task Transfer_sends_an_id_that_arrives() =>
        Check(async (fabrik, einstellungen) =>
            await new WechselPost.HttpNotifier(
                fabrik,
                Options.Create(new WechselPost.NotificationSettings
                {
                    Adresse = einstellungen,
                    Geheimnis = "probe"
                }))
                .DeliverAsync(new SubjectId(Empfaenger), "market_request"));

    /// <summary>The body of applications-service.</summary>
    [Fact]
    public Task Applications_send_an_id_that_arrives() =>
        Check(async (fabrik, einstellungen) =>
            await new AnwendungenPost.HttpBenachrichtigung(
                fabrik,
                Options.Create(new AnwendungenPost.NotificationSettings
                {
                    Adresse = einstellungen,
                    Geheimnis = "probe"
                }))
                .DeliverAsync(new SubjectId(Empfaenger), "application_status"));

    /// <summary>The body of resume-service.</summary>
    [Fact]
    public Task Resume_sends_an_id_that_arrives() =>
        Check(async (fabrik, einstellungen) =>
            await new LebenslaufPost.HttpBenachrichtigung(
                fabrik,
                Options.Create(new LebenslaufPost.NotificationSettings
                {
                    Adresse = einstellungen,
                    Geheimnis = "probe"
                }))
                .DeliverAsync(new SubjectId(Empfaenger), "resume_request"));

    /// <summary>The body of scout-service.</summary>
    /// <remarks>
    /// The only one of the four that sends a <em>typed</em> contract from the
    /// start (<c>DiscoveryNotificationV1</c>) instead of an anonymous object —
    /// and it is checked here all the same. A type annotation says only that the
    /// sender thought about something; whether it arrives at the receiver is
    /// said by this run alone.
    /// </remarks>
    [Fact]
    public Task Scout_sends_an_id_that_arrives() =>
        Check(async (fabrik, einstellungen) =>
            await new SpaeherPost.HttpBenachrichtigung(
                fabrik,
                Options.Create(new SpaeherPost.NotificationSettings
                {
                    Adresse = einstellungen,
                    Geheimnis = "probe"
                }))
                .DeliverAsync(new SubjectId(Empfaenger), "profile_discovered"));

    /// <summary>
    /// And the fourth hop: <c>notification-service</c> asks
    /// <c>identity-service</c> to really send the mail.
    /// </summary>
    /// <remarks>
    /// The quietest of the four. The three above run over the outbox and leave
    /// ten attempts and an error column behind on a failure; this one does not
    /// throw but logs a warning. It can therefore be broken for arbitrarily long
    /// without anything going red anywhere — and it was: in the inbox stood
    /// nothing but the confirmation mails <c>identity-service</c> sends itself.
    /// <para>
    /// The receiver type <c>NotifyBodyV1</c> is <b>private</b>. It is therefore
    /// fetched through reflection rather than rebuilt: a rebuilt contract in the
    /// test would be a second truth, and the two would go separate ways at the
    /// next rename — exactly the divergence this test exists to expose.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Notification_asks_identity_with_an_id_that_arrives()
    {
        var lauscher = new Lauscher();

        await new HttpMailer(
            new Stubfabrik(lauscher),
            Options.Create(new MailSettings
            {
                Adresse = "http://probe.invalid",
                Geheimnis = "probe"
            }),
            NullLogger<HttpMailer>.Instance)
            .SendAsync(new SubjectId(Empfaenger));

        lauscher.Rumpf.Should().NotBeNull("es wurde nichts geschickt");

        var contract = typeof(WorkerTransfer.Identity.Api.NotifyEndpoints).Assembly
            .GetTypes()
            .Single(type => type.Name == "NotifyBodyV1");

        var gelesen = JsonSerializer.Deserialize(lauscher.Rumpf!, contract);

        gelesen.Should().NotBeNull();
        contract.GetProperty("UserId")!.GetValue(gelesen).Should().Be(
            Empfaenger,
            "identity liest den Namen, den NotifyBodyV1 deklariert — kommt die "
            + "Kennung anders, bindet sie still auf Guid.Empty und es geht keine "
            + "Mail hinaus, ohne dass irgendetwas rot wird");
    }

    /// <summary>
    /// Captures the body and reads it with the receiver's contract.
    /// </summary>
    /// <remarks>
    /// Deserialisation happens WITHOUT options of its own: that is exactly how
    /// ASP.NET reads it at the endpoint. Whoever set `PropertyNameCaseInsensitive`
    /// here would make the test more lenient than reality — and `userId` would
    /// get through again.
    /// </remarks>
    private static async Task Check(Func<IHttpClientFactory, string, Task> senden)
    {
        var lauscher = new Lauscher();

        await senden(new Stubfabrik(lauscher), "http://probe.invalid");

        lauscher.Rumpf.Should().NotBeNull("es wurde nichts geschickt");

        var gelesen = JsonSerializer.Deserialize<NotifyV1>(lauscher.Rumpf!);

        gelesen.Should().NotBeNull();
        gelesen!.UserId.Should().Be(
            Empfaenger,
            "der Empfaenger liest `user_id` — kommt die Kennung als etwas anderes, "
            + "bindet sie still auf Guid.Empty und der Dienst antwortet 500");
        gelesen.Kind.Should().NotBeNullOrEmpty("ohne Art gibt es 422");
    }

    /// <summary>Takes the body and acknowledges like the real endpoint.</summary>
    private sealed class Lauscher : HttpMessageHandler
    {
        public string? Rumpf { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Rumpf = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }
    }

    private sealed class Stubfabrik(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
