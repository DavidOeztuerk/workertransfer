namespace WorkerTransfer.Identity.Infrastructure.Post;

/// <summary>Where mail goes and what the link in it points at.</summary>
public sealed class Postsettings
{
    /// <summary>The configuration section this binds to.</summary>
    public const string Abschnitt = "Mail";

    /// <summary>The SMTP host.</summary>
    public string Host { get; set; } = "localhost";

    /// <summary>The SMTP port. 1025 is Mailpit's.</summary>
    public int Port { get; set; } = 1025;

    /// <summary>What the mail is sent as.</summary>
    public string Absender { get; set; } = "noreply@workertransfer.local";

    /// <summary>Set only where the server actually wants it.</summary>
    public string? Benutzer { get; set; }

    /// <inheritdoc cref="Benutzer" />
    public string? Passwort { get; set; }

    /// <summary>Whether to upgrade the connection.</summary>
    public bool Tls { get; set; }

    /// <summary>
    /// The base address of the confirmation link.
    /// </summary>
    /// <remarks>
    /// Has to be the address the browser sees, not a compose service name — the
    /// link is clicked by a person, from a mail client, outside every network
    /// this service knows about.
    /// </remarks>
    public string WebAdresse { get; set; } = "http://localhost:5173";
}
