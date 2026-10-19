namespace WorkerTransfer.Identity.Infrastructure.Mail;

/// <summary>Where mail goes and what the link in it points at.</summary>
public sealed class MailSettings
{
    /// <summary>The configuration section this binds to.</summary>
    public const string Section = "Mail";

    /// <summary>The SMTP host.</summary>
    public string Host { get; set; } = "localhost";

    /// <summary>The SMTP port. 1025 is Mailpit's.</summary>
    public int Port { get; set; } = 1025;

    /// <summary>What the mail is sent as.</summary>
    /// <remarks>
    /// The property name IS the environment key (<c>Mail__Sender</c>); renaming it
    /// alone leaves the value on its default, silently.
    /// </remarks>
    public string Sender { get; set; } = "noreply@workertransfer.local";

    /// <summary>Set only where the server actually wants it.</summary>
    public string? Account { get; set; }

    /// <inheritdoc cref="Account" />
    public string? Password { get; set; }

    /// <summary>Whether to upgrade the connection.</summary>
    public bool Tls { get; set; }

    /// <summary>
    /// The base address of the confirmation link.
    /// </summary>
    /// <remarks>
    /// Has to be the address the browser sees, not a compose service name — the
    /// link is clicked by a person, from a mail client, outside every network
    /// this service knows about.
    /// <para>
    /// The property name IS the environment key (<c>Mail__WebUrl</c>); renaming it
    /// alone leaves the value on its default, silently.
    /// </para>
    /// </remarks>
    public string WebUrl { get; set; } = "http://localhost:5173";
}
