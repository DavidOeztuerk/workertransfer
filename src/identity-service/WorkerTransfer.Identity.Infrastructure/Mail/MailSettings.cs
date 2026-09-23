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
    /// The property name IS the environment key (<c>Mail__Absender</c>), so it
    /// stays German until the keys are renamed everywhere at once — in compose,
    /// in the chart and in <c>.env.example</c>.
    /// </remarks>
    public string Absender { get; set; } = "noreply@workertransfer.local";

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
    /// The property name IS the environment key (<c>Mail__WebAdresse</c>), so it
    /// stays German until the keys are renamed everywhere at once — in compose,
    /// in the chart and in <c>.env.example</c>.
    /// </para>
    /// </remarks>
    public string WebAdresse { get; set; } = "http://localhost:5173";
}
