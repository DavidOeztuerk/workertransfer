using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Noelia.Core.Identity;
using Noelia.Infrastructure.Security.M2M;
using WorkerTransfer.Notification.Infrastructure.Mail;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.CrossCutting.Tests;

/// <summary>
/// What a door client does when it cannot get a token (ADR-0057): it goes the
/// way of a service that is silent, and it never reaches the wire without one.
/// </summary>
public sealed class ServiceCallTests
{
    private sealed class Wire : HttpMessageHandler
    {
        public HttpRequestMessage? Seen { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Seen = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class Source(Func<string, string> answer) : IServiceTokenSource
    {
        public Task<string> GetTokenAsync(string audience, CancellationToken cancellationToken = default) =>
            Task.FromResult(answer(audience));
    }

    private static HttpClient Client(Wire wire, IServiceTokenSource source)
    {
        var services = new ServiceCollection();
        services.AddSingleton(source);
        services.AddHttpClient("door")
            .AddServiceCall(ServiceAudiences.Identity)
            .ConfigurePrimaryHttpMessageHandler(() => wire);

        return services.BuildServiceProvider()
            .GetRequiredService<IHttpClientFactory>().CreateClient("door");
    }

    [Fact]
    public async Task A_call_carries_the_token_of_its_audience()
    {
        var wire = new Wire();

        await Client(wire, new Source(audience => $"token-for-{audience}"))
            .GetAsync(new Uri("http://identity-service:8001/internal/notify"));

        wire.Seen!.Headers.Authorization!.ToString().Should().Be("Bearer token-for-identity");
    }

    /// <summary>
    /// Every door caller catches <see cref="HttpRequestException"/> and turns it
    /// into its silence: a role lookup into a 503. The exception Noelia throws is
    /// not one, so uncaught it would be a 500.
    /// </summary>
    [Fact]
    public async Task No_token_reads_as_a_silent_service()
    {
        var wire = new Wire();
        var client = Client(wire, new Source(_ => throw new ServiceTokenUnavailableException("none")));

        var call = () => client.GetAsync(new Uri("http://identity-service:8001/internal/notify"));

        await call.Should().ThrowAsync<HttpRequestException>();
        wire.Seen.Should().BeNull("without a token nothing goes out");
    }

    /// <summary>A time limit stays a time limit: the callers tell the two apart.</summary>
    [Fact]
    public async Task A_cancelled_call_stays_cancelled()
    {
        var client = Client(new Wire(), new Source(_ => throw new OperationCanceledException()));

        var call = () => client.GetAsync(new Uri("http://identity-service:8001/internal/notify"));

        await call.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class Stalled : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new TaskCanceledException("the time limit ran out");
    }

    private sealed class StalledFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Stalled());
    }

    private static HttpMailer Mailer() =>
        new(
            new StalledFactory(),
            Options.Create(new MailSettings { BaseUrl = "http://identity-service:8001" }),
            NullLogger<HttpMailer>.Instance);

    /// <summary>
    /// The mailer never topples the case that triggered it, and a time limit is
    /// a failure like any other: through it `/internal/notifications` answered 500.
    /// </summary>
    [Fact]
    public async Task A_time_limit_at_the_mailer_ends_quietly()
    {
        var send = () => Mailer().SendAsync(new SubjectId(Guid.CreateVersion7()));

        await send.Should().NotThrowAsync();
    }

    /// <summary>The caller's own abort stays one.</summary>
    [Fact]
    public async Task An_abort_by_the_caller_reaches_the_caller()
    {
        using var abort = new CancellationTokenSource();
        await abort.CancelAsync();

        var send = () => Mailer().SendAsync(new SubjectId(Guid.CreateVersion7()), abort.Token);

        await send.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// Forgetting the module would surface at the first call, outside the
    /// callers' <c>try</c> — a 500 in operation instead of a refusal at startup.
    /// </summary>
    [Fact]
    public async Task A_service_with_a_door_client_but_no_token_source_does_not_start()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddHttpClient("door").AddServiceCall(ServiceAudiences.Identity);

        await using var app = builder.Build();

        var start = () => app.StartAsync();

        (await start.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*UseServiceTokenClient*");
    }
}
