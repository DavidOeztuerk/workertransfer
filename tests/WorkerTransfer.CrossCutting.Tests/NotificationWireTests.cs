using System.Net;
using System.Text.Json;
using FluentAssertions;
using Noelia.Core.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WorkerTransfer.Notification.Contracts;
using WorkerTransfer.Notification.Infrastructure.Mail;
using ApplicationsPost = WorkerTransfer.Applications.Infrastructure.Notification;
using ResumePost = WorkerTransfer.Resume.Infrastructure.Notification;
using ScoutPost = WorkerTransfer.Scout.Infrastructure.Notification;
using TransferPost = WorkerTransfer.Transfer.Infrastructure.Notification;

namespace WorkerTransfer.CrossCutting.Tests;

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
/// <c>WireContractTests</c> reads declared properties in the API and contracts
/// assemblies. Here the body stands as an <em>anonymous object</em> in the
/// infrastructure — no declared type, so nothing to check. And no service suite
/// crosses the seam: in the tests the sender has no receiver, the receiver no
/// sender.</para>
/// </remarks>
public class NotificationWireTests
{
    private static readonly Guid Recipient = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301");

    /// <summary>The body of transfer-service.</summary>
    [Fact]
    public Task Transfer_sends_an_id_that_arrives() =>
        Check(async (factory, address) =>
            await new TransferPost.HttpNotifier(
                factory,
                Options.Create(new TransferPost.NotificationSettings
                {
                    BaseUrl = address,
                    Secret = "probe"
                }))
                .DeliverAsync(new SubjectId(Recipient), "market_request"));

    /// <summary>The body of applications-service.</summary>
    [Fact]
    public Task Applications_send_an_id_that_arrives() =>
        Check(async (factory, address) =>
            await new ApplicationsPost.HttpNotifier(
                factory,
                Options.Create(new ApplicationsPost.NotificationSettings
                {
                    BaseUrl = address,
                    Secret = "probe"
                }))
                .DeliverAsync(new SubjectId(Recipient), "application_status"));

    /// <summary>The body of resume-service.</summary>
    [Fact]
    public Task Resume_sends_an_id_that_arrives() =>
        Check(async (factory, address) =>
            await new ResumePost.HttpNotifier(
                factory,
                Options.Create(new ResumePost.NotificationSettings
                {
                    BaseUrl = address,
                    Secret = "probe"
                }))
                .DeliverAsync(new SubjectId(Recipient), "resume_request"));

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
        Check(async (factory, address) =>
            await new ScoutPost.HttpNotifier(
                factory,
                Options.Create(new ScoutPost.NotificationSettings
                {
                    BaseUrl = address,
                    Secret = "probe"
                }))
                .DeliverAsync(new SubjectId(Recipient), "profile_discovered"));

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
        var listener = new Listener();

        await new HttpMailer(
            new StubFactory(listener),
            Options.Create(new MailSettings
            {
                BaseUrl = "http://probe.invalid",
                Secret = "probe"
            }),
            NullLogger<HttpMailer>.Instance)
            .SendAsync(new SubjectId(Recipient));

        listener.Body.Should().NotBeNull("nothing was sent");

        var contract = typeof(WorkerTransfer.Identity.Api.NotifyEndpoints).Assembly
            .GetTypes()
            .Single(type => type.Name == "NotifyBodyV1");

        var read = JsonSerializer.Deserialize(listener.Body!, contract);

        read.Should().NotBeNull();
        contract.GetProperty("UserId")!.GetValue(read).Should().Be(
            Recipient,
            "identity reads the name NotifyBodyV1 declares — if the id arrives "
            + "under another one, it binds silently to Guid.Empty and no mail goes "
            + "out, without anything turning red");
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
    private static async Task Check(Func<IHttpClientFactory, string, Task> send)
    {
        var listener = new Listener();

        await send(new StubFactory(listener), "http://probe.invalid");

        listener.Body.Should().NotBeNull("nothing was sent");

        var read = JsonSerializer.Deserialize<NotifyV1>(listener.Body!);

        read.Should().NotBeNull();
        read!.UserId.Should().Be(
            Recipient,
            "the receiver reads `user_id` — if the id arrives as anything else, "
            + "it binds silently to Guid.Empty and the service answers 500");
        read.Kind.Should().NotBeNullOrEmpty("without a kind the answer is 422");
    }

    /// <summary>Takes the body and acknowledges like the real endpoint.</summary>
    private sealed class Listener : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
