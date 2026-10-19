using System.Net;
using System.Net.Sockets;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Noelia.Infrastructure.Sovereignty;
using WorkerTransfer.Applications.Application.Ports;
using WorkerTransfer.Applications.Infrastructure.CoverLetter;

namespace WorkerTransfer.Applications.Tests;

/// <summary>
/// A person's model address that redirects is refused by name, and nothing
/// travels on.
/// </summary>
/// <remarks>
/// <para>The address is the person's own (<c>AiAccessV1</c>), and the request
/// carries their key and their profile text. Before Noelia 7.0.0 the factory
/// client followed a 307/308 and resent both to the Location. Now the redirect
/// comes back, and the writer says it is a redirect — not "answers with 307",
/// which reads like a broken provider.</para>
///
/// <para>Real sockets and Noelia's real guard: the second listener is how it is
/// shown that the body and the key did not arrive anywhere else.</para>
/// </remarks>
public sealed class CoverLetterRedirectTests
{
    [Theory]
    [InlineData(307)]
    [InlineData(308)]
    [InlineData(301)]
    public async Task A_redirecting_provider_is_named_and_not_followed(int code)
    {
        var elsewhere = 0;
        using var target = Listen(_ => Interlocked.Increment(ref elsewhere), 200, null);
        using var provider = Listen(_ => { }, code, $"http://localhost:{target.Port}/v1/chat/completions");

        var services = new ServiceCollection();
        services.AddHttpClient();
        services.AddNoeliaEgressPolicy(policy => policy.Allow("127.0.0.1", "localhost"));

        var writer = new HttpCoverLetterWriter(
            services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>(),
            Options.Create(new CoverLetterSettings { Timeout = TimeSpan.FromSeconds(10) }));

        var act = () => writer.WriteAsync(
            new AiAccess(
                "openai_compatible", $"http://127.0.0.1:{provider.Port}/v1/chat/completions",
                "a-model", "a-persons-key"),
            new CoverLetterContext(
                "Title", "Company", "Place", "Description", [], "Name", "Headline",
                "About", [], [], "de"));

        await act.Should().ThrowAsync<CoverLetterUnavailable>()
            .WithMessage($"*redirects ({code})*");
        elsewhere.Should().Be(0, "the key and the profile text must not be resent");
    }

    private sealed class Listener(HttpListener inner, int port) : IDisposable
    {
        public int Port { get; } = port;

        public void Dispose() => inner.Close();
    }

    private static Listener Listen(Action<HttpListenerContext> seen, int code, string? location)
    {
        int port;

        using (var probe = new TcpListener(IPAddress.Loopback, 0))
        {
            probe.Start();
            port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
        }

        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();

        _ = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                HttpListenerContext context;

                try
                {
                    context = await listener.GetContextAsync();
                }
                catch (Exception error) when (error is HttpListenerException or ObjectDisposedException)
                {
                    return;
                }

                seen(context);
                context.Response.StatusCode = code;

                if (location is not null)
                {
                    context.Response.RedirectLocation = location;
                }

                context.Response.Close();
            }
        });

        return new Listener(listener, port);
    }
}
