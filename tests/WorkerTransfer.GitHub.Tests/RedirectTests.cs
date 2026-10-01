using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Noelia.Infrastructure.Sovereignty;
using WorkerTransfer.GitHub.Application.Ports;
using WorkerTransfer.GitHub.Infrastructure.Network;

namespace WorkerTransfer.GitHub.Tests;

/// <summary>A real socket that answers a path with a redirect — and records who came.</summary>
public sealed class RedirectingListener : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Func<string, (int Code, string? Location, string Body)> _answer;

    public RedirectingListener(Func<string, (int Code, string? Location, string Body)> answer)
    {
        _answer = answer;

        // A free port, then the listener on it.
        using (var probe = new TcpListener(IPAddress.Loopback, 0))
        {
            probe.Start();
            Port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
        }

        _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
        _listener.Prefixes.Add($"http://localhost:{Port}/");
        _listener.Start();
        _ = Task.Run(ServeAsync);
    }

    public int Port { get; }

    public string Origin => $"http://127.0.0.1:{Port}";

    /// <summary>Path and authorization header of every request that arrived.</summary>
    public List<(string Path, string? Authorization)> Arrived { get; } = [];

    private async Task ServeAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;

            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception error) when (error is HttpListenerException or ObjectDisposedException)
            {
                return;
            }

            var path = context.Request.Url!.PathAndQuery;

            lock (Arrived)
            {
                Arrived.Add((path, context.Request.Headers["Authorization"]));
            }

            var (code, location, body) = _answer(path);
            context.Response.StatusCode = code;

            if (location is not null)
            {
                context.Response.RedirectLocation = location;
            }

            var bytes = Encoding.UTF8.GetBytes(body);
            context.Response.ContentType = "application/json";
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }
    }

    public void Dispose() => _listener.Close();
}

/// <summary>
/// GitHub's redirects under the egress boundary — the client no longer follows
/// them, so <see cref="HttpGitHub"/> does, deliberately and only at home.
/// </summary>
/// <remarks>
/// <para>Since Noelia 7.0.0 an enforcing egress policy switches AutoRedirect off
/// for every factory client and hands 301/302/303/307/308 to the caller. A
/// renamed repository answers exactly that — measured 01.10.2026 at
/// api.github.com, <c>/repos/{old}/languages</c> → 301 →
/// <c>/repositories/{id}/languages</c>. Unhandled, the whole fetch fell to
/// "github returned 301", which reads like an outage at GitHub.</para>
///
/// <para>The factory here is the real one with Noelia's real guard on it, and
/// GitHub is a real socket — a canned handler would answer whatever the code
/// asks and could not show who actually followed.</para>
/// </remarks>
public sealed class RedirectTests
{
    private static HttpGitHub Build(string baseUrl, string token, params string[] allowed)
    {
        var services = new ServiceCollection();
        services.AddHttpClient();
        services.AddNoeliaEgressPolicy(policy => policy.Allow(allowed));

        var factory = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();

        return new HttpGitHub(
            factory,
            Options.Create(new GitHubSettings { BaseUrl = baseUrl, Token = token }),
            NullLogger<HttpGitHub>.Instance);
    }

    [Fact]
    public async Task A_renamed_repository_is_followed_within_the_API()
    {
        using var github = new RedirectingListener(path => path switch
        {
            "/users/anna/repos?per_page=100&sort=pushed&type=owner" =>
                (200, null, """[{"name":"old-name","fork":false}]"""),
            "/repos/anna/old-name/languages" =>
                (301, "/repositories/42/languages", ""),
            "/repositories/42/languages" =>
                (200, null, """{"Go": 1200, "Shell": 30}"""),
            _ => (404, null, "")
        });

        var snapshot = await Build(github.Origin, "token-for-the-test", "127.0.0.1")
            .RepositoriesAsync("anna");

        snapshot.Repositories.Should().ContainSingle()
            .Which.Languages.Should().BeEquivalentTo(["Go", "Shell"]);

        // Followed by THIS code, once, with the token — the API's own origin.
        github.Arrived.Select(entry => entry.Path)
            .Should().Contain("/repositories/42/languages");
        github.Arrived.Should().OnlyContain(entry => entry.Authorization == "Bearer token-for-the-test");
    }

    [Fact]
    public async Task A_redirect_to_another_origin_is_not_followed()
    {
        using var elsewhere = new RedirectingListener(_ => (200, null, "{}"));
        using var github = new RedirectingListener(path =>
            path.EndsWith("/languages", StringComparison.Ordinal)
                ? (307, $"http://localhost:{elsewhere.Port}/collect", "")
                : (200, null, """[{"name":"repo","fork":false}]"""));

        // Both hosts are allowed: the refusal must come from the code, not the guard.
        var act = () => Build(github.Origin, "token-for-the-test", "127.0.0.1", "localhost")
            .RepositoriesAsync("anna");

        await act.Should().ThrowAsync<GitHubSilent>().WithMessage("*another origin*");
        elsewhere.Arrived.Should().BeEmpty("the token must not travel to a foreign origin");
    }

    [Fact]
    public async Task The_factory_client_itself_does_not_follow()
    {
        // The premise of the two above, measured instead of read: without our
        // handling the redirect arrives at the caller.
        using var target = new RedirectingListener(_ => (200, null, "{}"));
        using var origin = new RedirectingListener(_ => (301, $"{target.Origin}/x", ""));

        var services = new ServiceCollection();
        services.AddHttpClient();
        services.AddNoeliaEgressPolicy(policy => policy.Allow("127.0.0.1"));

        using var client = services.BuildServiceProvider()
            .GetRequiredService<IHttpClientFactory>().CreateClient("probe");
        client.Timeout = TimeSpan.FromSeconds(10);

        using var response = await client.GetAsync($"{origin.Origin}/start");

        response.StatusCode.Should().Be(HttpStatusCode.MovedPermanently);
        target.Arrived.Should().BeEmpty();
    }
}
