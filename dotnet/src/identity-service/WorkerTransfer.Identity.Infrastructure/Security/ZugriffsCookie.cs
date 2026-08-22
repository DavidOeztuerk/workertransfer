using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace WorkerTransfer.Identity.Infrastructure.Security;

/// <summary>
/// Lets the access token arrive as a cookie as well as a header.
/// </summary>
/// <remarks>
/// Both carriers are needed and neither is optional. Service-to-service and CLI
/// callers send <c>Authorization: Bearer</c>; a browser never sees the token at
/// all — it is <c>httpOnly</c>, and the only thing the page can do is let the
/// cookie ride along. Without this, every request from the app is anonymous
/// while the sign-in looks like it worked.
/// </remarks>
public static class ZugriffsCookie
{
    /// <summary>The cookie the sign-in endpoints set.</summary>
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
