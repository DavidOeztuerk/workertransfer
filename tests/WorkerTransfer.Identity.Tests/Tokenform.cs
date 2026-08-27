using System.Text.Json;
using Girder.Core.Identity;
using Girder.Infrastructure.Models;
using Girder.Infrastructure.Security;
using Girder.Infrastructure.Security.Keys;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WorkerTransfer.Identity.Infrastructure.Security;

namespace WorkerTransfer.Identity.Tests;

/// <summary>
/// Builds the token issuer the way the composition root does, and reads the
/// payload back as JSON.
/// </summary>
internal static class Tokenform
{
    internal const string Secret = "test-secret-with-at-least-thirty-two-bytes-xx";
    internal const string Issuer = "workertransfer-identity";
    internal const string Audience = "workertransfer";

    internal static GirderAccessTokenIssuer Issuer_()
    {
        var settings = new JwtSettings
        {
            Secret = Secret,
            Issuer = Issuer,
            Audience = Audience,
            ExpireMinutes = 15
        };
        var shared = SigningKey.FromSharedSecret(Secret, kid: null);

        return new GirderAccessTokenIssuer(new JwtService(
            Options.Create(settings),
            new KeyRing([shared], shared),
            NullLogger<JwtService>.Instance));
    }

    internal static JsonElement Payload(string jwt)
    {
        var part = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
        var padded = part.PadRight(part.Length + (4 - part.Length % 4) % 4, '=');
        return JsonDocument.Parse(Convert.FromBase64String(padded)).RootElement;
    }

    internal static string? Claim(this JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value) ? value.ToString() : null;

    internal static bool Has(this JsonElement payload, string name) =>
        payload.TryGetProperty(name, out _);
}
