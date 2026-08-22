using Girder.Core.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Identity.Application.Ports;

namespace WorkerTransfer.Identity.Infrastructure.Post;

/// <summary>One mail, waiting for the commit.</summary>
/// <param name="An">Where it goes.</param>
/// <param name="Betreff">Its subject.</param>
/// <param name="Text">Its body.</param>
/// <param name="Empfaenger">
/// Whose account it is. For the log, so a failure does not write the address
/// into it — the audit allowlist excludes addresses for exactly this reason.
/// </param>
public sealed record AusgehendePost(
    string An, string Betreff, string Text, SubjectId Empfaenger);

/// <summary>Collects mail during a request and sends it afterwards.</summary>
/// <remarks>
/// Scoped, so the tray belongs to one request and cannot leak into the next.
/// Both halves of the port are on one class because they are one tray; they are
/// two interfaces so a handler can only ever put something in.
/// </remarks>
public sealed class Postkorb(
    IVersender versender,
    IOptions<Postsettings> einstellungen,
    ILogger<Postkorb> protokoll) : IPostkorb, IPostkorbVersand
{
    private readonly List<AusgehendePost> _wartend = [];
    private readonly Postsettings _einstellungen = einstellungen.Value;

    /// <inheritdoc />
    public void Bestaetigungslink(string an, SubjectId empfaenger, string klartextToken)
    {
        var basis = _einstellungen.WebAdresse.TrimEnd('/');

        _wartend.Add(new AusgehendePost(
            an,
            "Bitte bestätige deine E-Mail-Adresse",
            "Willkommen bei WorkerTransfer! Bitte bestätige deine E-Mail-Adresse "
            + $"über folgenden Link:\n\n{basis}/verify?token={klartextToken}\n",
            empfaenger));
    }

    /// <inheritdoc />
    public void Doppelanmeldung(string an, SubjectId empfaenger) =>
        _wartend.Add(new AusgehendePost(
            an,
            "Registrierungsversuch mit deiner E-Mail-Adresse",
            "Jemand hat versucht, mit deiner E-Mail-Adresse ein neues Konto bei "
            + "WorkerTransfer anzulegen. Du hast bereits ein Konto — falls du das "
            + "warst, melde dich einfach an. War es nicht du, kannst du diese "
            + "Nachricht ignorieren.\n",
            empfaenger));

    /// <inheritdoc />
    public void Einladung(string an, SubjectId einladender, string firma, string klartextToken)
    {
        var basis = _einstellungen.WebAdresse.TrimEnd('/');
        var wen = string.IsNullOrWhiteSpace(firma) ? "ein Unternehmen" : firma;

        _wartend.Add(new AusgehendePost(
            an,
            "Du wurdest zu einem Unternehmen eingeladen",
            $"Du wurdest eingeladen, für {wen} bei WorkerTransfer zu handeln. "
            + "Über folgenden Link nimmst du die Einladung an:\n\n"
            + $"{basis}/invitation?token={klartextToken}\n\n"
            + "Wenn du damit nichts anfangen kannst, ignoriere diese Nachricht.\n",
            einladender));
    }

    /// <inheritdoc />
    public void Loeschbestaetigung(string an, SubjectId wer) =>
        _wartend.Add(new AusgehendePost(
            an,
            "Dein Konto bei WorkerTransfer ist gelöscht",
            "Dein Konto und die Daten, die andere Dienste über dich hielten, sind "
            + "gelöscht. Es gibt nichts mehr, das dich hier zuordnet.\n\n"
            + "Diese Nachricht ist die letzte, die du von uns bekommst.\n",
            wer));

    /// <inheritdoc />
    public async Task VersendeGesammeltesAsync(CancellationToken cancellationToken = default)
    {
        // Taken out first: a send that throws must not leave the tray full for
        // whatever runs next in this scope.
        var abzuschicken = _wartend.ToArray();
        _wartend.Clear();

        foreach (var post in abzuschicken)
        {
            try
            {
                await versender.SendeAsync(post, cancellationToken);
            }
            catch (Exception fehler)
            {
                protokoll.LogError(
                    fehler, "Mail an Konto {Konto} konnte nicht zugestellt werden",
                    post.Empfaenger);
            }
        }
    }
}
