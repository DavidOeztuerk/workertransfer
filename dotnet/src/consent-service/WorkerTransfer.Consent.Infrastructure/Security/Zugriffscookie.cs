using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace WorkerTransfer.Consent.Infrastructure.Security;

/// <summary>
/// Lets the access token arrive as a cookie as well as a header.
/// </summary>
/// <remarks>
/// Both carriers are needed and neither is optional. Consuming services and CLI
/// callers send <c>Authorization: Bearer</c>; a browser never sees the token at
/// all — it is <c>httpOnly</c>, and the only thing the page can do is let the
/// cookie ride along. Without this, the consent page is anonymous to this
/// service while the sign-in looks like it worked, and every switch on it comes
/// back <c>401</c>.
/// <para>
/// Copied from identity-service rather than shared: thirty lines lifted into a
/// common package would be a coupling point across a service boundary, and its
/// price is higher than the copy's (ADR-0004). What must not be copied is the
/// <em>meaning</em> — which cookie a service reads is its own decision, and
/// this one only ever reads, never sets.
/// </para>
/// </remarks>
public static class Zugriffscookie
{
    /// <summary>The cookie identity-service sets on sign-in.</summary>
    public const string Name = "access";

    /// <summary>Reads the token out of the cookie when no header carried one.</summary>
    /// <remarks>
    /// Header first, deliberately: a caller that went to the trouble of sending
    /// one means it, and a stale cookie must not quietly win over it.
    /// </remarks>
    public static JwtBearerOptions AuchAusDemCookie(this JwtBearerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var vorher = options.Events?.OnMessageReceived;

        options.Events ??= new JwtBearerEvents();
        options.Events.OnMessageReceived = async context =>
        {
            if (vorher is not null)
            {
                await vorher(context);
            }

            if (string.IsNullOrEmpty(context.Token)
                && context.Request.Cookies.TryGetValue(Name, out var ausCookie)
                && !string.IsNullOrEmpty(ausCookie))
            {
                context.Token = ausCookie;
            }
        };

        return options;
    }
}
