using Noelia.Core.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WorkerTransfer.Identity.Application.Ports;
using WorkerTransfer.Identity.Domain.Users;

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
    public void Bestaetigungslink(
        string an, SubjectId recipient, string klartextToken, Kontosprache language)
    {
        var basis = _einstellungen.WebAdresse.TrimEnd('/');
        var text = Mailtexte.Bestaetigung(
            language, $"{basis}/verify?token={klartextToken}");

        _wartend.Add(new AusgehendePost(an, text.Betreff, text.Text, recipient));
    }

    /// <inheritdoc />
    public void Doppelanmeldung(string an, SubjectId recipient, Kontosprache language)
    {
        var text = Mailtexte.Doppelanmeldung(language);
        _wartend.Add(new AusgehendePost(an, text.Betreff, text.Text, recipient));
    }

    /// <inheritdoc />
    public void Einladung(
        string an, SubjectId einladender, string company, string klartextToken,
        Kontosprache language)
    {
        var basis = _einstellungen.WebAdresse.TrimEnd('/');
        var wen = string.IsNullOrWhiteSpace(company)
            ? Mailtexte.EinUnternehmen(language)
            : company;
        var text = Mailtexte.Einladung(
            language, wen, $"{basis}/invitation?token={klartextToken}");

        _wartend.Add(new AusgehendePost(an, text.Betreff, text.Text, einladender));
    }

    /// <inheritdoc />
    public void Loeschbestaetigung(string an, SubjectId subject, Kontosprache language)
    {
        var text = Mailtexte.Loeschbestaetigung(language);
        _wartend.Add(new AusgehendePost(an, text.Betreff, text.Text, subject));
    }

    /// <inheritdoc />
    public void Neuigkeit(string an, SubjectId subject, Kontosprache language)
    {
        var basis = _einstellungen.WebAdresse.TrimEnd('/');
        var text = Mailtexte.Neuigkeit(language, basis);

        _wartend.Add(new AusgehendePost(an, text.Betreff, text.Text, subject));
    }

    /// <inheritdoc />
    public async Task SendCollectedAsync(CancellationToken cancellationToken = default)
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
            catch (Exception error)
            {
                protokoll.LogError(
                    error, "Mail an Konto {Konto} konnte nicht zugestellt werden",
                    post.Empfaenger);
            }
        }
    }
}
