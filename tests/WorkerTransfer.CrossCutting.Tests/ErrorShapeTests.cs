using System.Net;
using System.Text.Json;
using FluentAssertions;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.CrossCutting.Tests;

/// <summary>
/// What <see cref="ProblemDetailsMiddleware"/> makes of a thrown exception.
/// </summary>
/// <remarks>
/// Here and not in a service, because the middleware lies in <c>src/shared</c>
/// and applies to all eleven: one shape across all services, so that a caller
/// does not have to know who answered in order to read the error.
/// <para>
/// Both branches here were found in operation, not in the design: a truncated
/// JSON body came back as a 500 at <em>every</em> endpoint, and Noelia's
/// <c>ValidationBehavior</c> throws a <c>ValidationException</c> that would
/// likewise have become a 500 without a branch of its own — at the first
/// validator somebody writes.
/// </para>
/// </remarks>
public sealed class ErrorShapeTests
{
    private static WebApplication BuildHost(Func<Task> action)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        var host = builder.Build();
        host.UseMiddleware<ProblemDetailsMiddleware>();
        host.Map("/throw", async () => await action());

        return host;
    }

    private static async Task<(HttpStatusCode Code, JsonElement Body, string? Type)> Ask(
        WebApplication host, HttpRequestMessage request)
    {
        using var browser = new HttpClient { BaseAddress = new Uri(host.Urls.First()) };
        using var response = await browser.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();

        return (response.StatusCode,
                JsonDocument.Parse(raw).RootElement,
                response.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>
    /// <strong>An aborted call is not a server error.</strong>
    /// </summary>
    /// <remarks>
    /// Measured on an E2E run: <strong>853 of 866</strong> error lines in the
    /// whole stack were exactly that. The browser aborts requests as soon as a
    /// page changes or a fetch supersedes an older one (<c>AbortController</c>)
    /// — and every single one became an "Unhandled request exception" together
    /// with a 500. The thirteen real errors stood in between and could not be
    /// found.
    /// <para>
    /// The check runs over a real abort, not over a thrown exception: only that
    /// way is <c>context.RequestAborted</c> really pulled, and the distinction
    /// from our own timeout hangs on exactly that.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task An_aborted_call_does_not_become_a_server_error()
    {
        var log = new CollectingLog();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(log);

        var host = builder.Build();
        host.UseMiddleware<ProblemDetailsMiddleware>();
        host.Map("/slow", async (HttpContext context) =>
            await Task.Delay(TimeSpan.FromSeconds(30), context.RequestAborted));

        await host.StartAsync();

        try
        {
            using var browser = new HttpClient { BaseAddress = new Uri(host.Urls.First()) };
            using var cancellationToken = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

            var attempt = async () => await browser.GetAsync("/slow", cancellationToken.Token);

            await attempt.Should().ThrowAsync<TaskCanceledException>(
                "the caller hangs up by itself");

            // Give the service time to process the abort.
            await Task.Delay(TimeSpan.FromMilliseconds(500));

            log.Errors.Should().NotContain(
                row => row.Contains("Unhandled request exception", StringComparison.Ordinal),
                "a caller who hangs up is no server error");
        }
        finally
        {
            await host.StopAsync();
        }
    }

    /// <summary>
    /// A timeout of OUR OWN stays a server error.
    /// </summary>
    /// <remarks>
    /// The condition that carries the whole thing: an <c>HttpClient</c> that
    /// gives up throws the same exception as an aborted call. Without the check
    /// on <c>context.RequestAborted</c> this case would disappear too — a
    /// genuine outage, silently.
    /// </remarks>
    [Fact]
    public async Task A_timeout_of_our_own_stays_a_server_error()
    {
        using var host = BuildHost(() => throw new OperationCanceledException("our time limit"));
        await host.StartAsync();

        try
        {
            var (code, body, _) = await Ask(
                host, new HttpRequestMessage(HttpMethod.Get, "/throw"));

            code.Should().Be(HttpStatusCode.InternalServerError);
            body.GetProperty("detail").GetString().Should().Be("An unexpected error occurred.");
        }
        finally
        {
            await host.StopAsync();
        }
    }

    /// <summary>
    /// An unreadable body is the caller's error, not a server error.
    /// </summary>
    /// <remarks>
    /// The exception is THROWN here rather than triggered by a truncated body:
    /// in a bare minimal host the framework catches the binding error itself and
    /// answers with an empty 400 before any middleware of our own sees it. In
    /// the real service it does arrive — measured there, on three services, 400
    /// with this document each time. What is checked here is the branch, not the
    /// path to it.
    /// </remarks>
    [Fact]
    public async Task A_broken_body_gives_400()
    {
        await using var host = BuildHost(() => throw new BadHttpRequestException(
            "Failed to read parameter \"ProfileBody body\" from the request body as JSON.",
            StatusCodes.Status400BadRequest));
        await host.StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/throw");
        var (code, body, type) = await Ask(host, request);

        code.Should().Be(HttpStatusCode.BadRequest);
        body.GetProperty("detail").GetString().Should().Be("malformed request body");
        type.Should().Be("application/problem+json");
    }

    /// <summary>
    /// The truncated body does not stand in the answer.
    /// </summary>
    /// <remarks>
    /// The message of <c>BadHttpRequestException</c> names the parameter and the
    /// byte position — that is already a statement about what somebody sent. It
    /// is therefore not passed on.
    /// </remarks>
    [Fact]
    public async Task The_broken_body_does_not_appear_in_the_answer()
    {
        await using var host = BuildHost(() => throw new BadHttpRequestException(
            "Failed to read parameter \"Remark\": appointment with Dr. Weber",
            StatusCodes.Status400BadRequest));
        await host.StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/throw");
        var (_, body, _) = await Ask(host, request);

        body.ToString().Should().NotContain("Weber");
        body.ToString().Should().NotContain("Remark");
    }

    /// <summary>A failed validation gives 422 and names the fields.</summary>
    [Fact]
    public async Task A_failed_validation_gives_422_with_field_names()
    {
        var error = new[]
        {
            new FluentValidation.Results.ValidationFailure("Email", "email is required"),
            new FluentValidation.Results.ValidationFailure("Password", "password is required")
        };

        await using var host = BuildHost(() => throw new ValidationException(error));
        await host.StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/throw");
        var (code, body, type) = await Ask(host, request);

        code.Should().Be(HttpStatusCode.UnprocessableEntity);
        body.GetProperty("detail").GetString().Should().Be("invalid: Email, Password");
        type.Should().Be("application/problem+json");
    }

    /// <summary>
    /// The answer names field names, never values.
    /// </summary>
    /// <remarks>
    /// FluentValidation's default messages insert placeholders such as
    /// <c>{PropertyValue}</c>. If the middleware passed <c>ErrorMessage</c> on,
    /// a person's value would stand in an error document — and with it in
    /// screenshots and bug reports.
    /// </remarks>
    [Fact]
    public async Task The_validation_answer_carries_no_value()
    {
        var error = new[]
        {
            new FluentValidation.Results.ValidationFailure(
                "Email", "'Email' has the value 'anna@example.org' and is invalid")
        };

        await using var host = BuildHost(() => throw new ValidationException(error));
        await host.StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/throw");
        var (_, body, _) = await Ask(host, request);

        body.ToString().Should().NotContain("anna");
        body.ToString().Should().NotContain("example.org");
        body.GetProperty("detail").GetString().Should().Be("invalid: Email");
    }

    /// <summary>Everything else stays 500 — a server error is one.</summary>
    [Fact]
    public async Task An_unexpected_exception_stays_500()
    {
        await using var host = BuildHost(() => throw new InvalidOperationException("broken"));
        await host.StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/throw");
        var (code, body, _) = await Ask(host, request);

        code.Should().Be(HttpStatusCode.InternalServerError);
        body.GetProperty("detail").GetString().Should().Be("An unexpected error occurred.");
        body.ToString().Should().NotContain("broken", "the exception message belongs in the log, not in the answer");
    }
}

/// <summary>A log that records only what was reported as an error.</summary>
internal sealed class CollectingLog : ILoggerProvider
{
    /// <summary>The messages of level "Error" or higher.</summary>
    public List<string> Errors { get; } = [];

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new Writer(Errors);

    /// <inheritdoc />
    public void Dispose() { }

    private sealed class Writer(List<string> error) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel < LogLevel.Error)
            {
                return;
            }

            lock (error)
            {
                error.Add(formatter(state, exception));
            }
        }
    }
}
