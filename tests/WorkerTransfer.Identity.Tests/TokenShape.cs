using System.Text.Json;
using Noelia.Core.Identity;
using Noelia.Infrastructure.Models;
using Noelia.Infrastructure.Security;
using Noelia.Infrastructure.Security.Keys;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WorkerTransfer.Identity.Infrastructure.Security;

namespace WorkerTransfer.Identity.Tests;

/// <summary>
/// Builds the token issuer the way the composition root does, and reads the
/// payload back as JSON.
/// </summary>
internal static class TokenShape
{
    /// <summary>The private test key (P-256, PKCS#8, base64).</summary>
    /// <remarks>
    /// Only identity-service holds it — here as in operation. The other
    /// thirteen services get <see cref="PublicKey"/> alone.
    /// </remarks>
    internal const string PrivateKey = "MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIBAQQgJ3H3ErtgAxTOpVVLh08LeYAzp06ePuF5MOe37hEIpZehRANCAATsDC5wPWJV4/HRgf8P2JwSrTF3mFASKSK7RsgkpBN97pH87mRpgy/rmLIjBqFZhq2C/VrcyfvLR2iev4OwNO7s";

    /// <summary>The matching public key (SPKI, base64).</summary>
    internal const string PublicKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE7AwucD1iVePx0YH/D9icEq0xd5hQEikiu0bIJKQTfe6R/O5kaYMv65iyIwahWYatgv1a3Mn7y0donr+DsDTu7A==";

    /// <summary>Names the key, so a rotation can hold two side by side.</summary>
    internal const string KeyId = "test";
    internal const string Issuer = "workertransfer-identity";
    internal const string Audience = "workertransfer";

    internal static NoeliaAccessTokenIssuer Issuer_()
    {
        var settings = new JwtSettings
        {
            Secret = string.Empty,
            Issuer = Issuer,
            Audience = Audience,
            ExpireMinutes = 15
        };
        var key = SigningKey.FromEcdsaPrivateKey(PrivateKey, KeyId);

        return new NoeliaAccessTokenIssuer(new JwtService(
            Options.Create(settings),
            new KeyRing([key], key),
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
