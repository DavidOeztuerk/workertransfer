using Girder.Core.Identity;
using WorkerTransfer.Identity.Application.Ports;

namespace WorkerTransfer.Identity.Application.Anmelden;

/// <summary>What a refresh produced.</summary>
public abstract record Erneuerungsergebnis
{
    private Erneuerungsergebnis() { }

    /// <summary>A fresh pair. The presented refresh token no longer counts.</summary>
    public sealed record Erneuert(
        string Zugriffstoken,
        string Erneuerungstoken,
        DateTimeOffset ErneuerungLaeuftAb) : Erneuerungsergebnis;

    /// <summary>
    /// The presented token entitles the caller to nothing.
    /// </summary>
    /// <remarks>
    /// Expired, already used, never issued, or belonging to an account that is
    /// gone — one answer for all of them, and the caller is told to drop the
    /// cookie it presented.
    /// </remarks>
    public sealed record Abgewiesen : Erneuerungsergebnis;
}

/// <summary>Carries a sign-in forward.</summary>
/// <remarks>
/// Rotation is the point: every refresh retires the token it was given, so a
/// stolen one works once and the second use is visible.
/// </remarks>
public sealed class ErneuernHandler(
    ISessionService sitzungen,
    IUserRepository benutzer,
    IAccessTokenIssuer token)
{
    /// <param name="erneuerungstoken">What the caller presented.</param>
    /// <param name="cancellationToken">Cancels the attempt.</param>
    public async Task<Erneuerungsergebnis> HandleAsync(
        string erneuerungstoken,
        CancellationToken cancellationToken = default)
    {
        var sitzung = await sitzungen.RenewAsync(erneuerungstoken, cancellationToken);

        if (sitzung is null)
        {
            return new Erneuerungsergebnis.Abgewiesen();
        }

        var konto = await benutzer.FindByIdAsync(sitzung.Subject, cancellationToken);

        if (konto is null)
        {
            return new Erneuerungsergebnis.Abgewiesen();
        }

        var zugriff = await token.IssueAsync(
            konto.Id,
            konto.Email,
            Capacity.AsSelf.Instance,
            sitzung.Session,
            cancellationToken);

        return new Erneuerungsergebnis.Erneuert(
            zugriff, sitzung.RefreshToken, sitzung.ExpiresAt);
    }
}

/// <summary>Ends a sign-in.</summary>
public sealed class AbmeldenHandler(ISessionService sitzungen)
{
    /// <summary>
    /// Ends the sign-in the token belongs to.
    /// </summary>
    /// <remarks>
    /// Answers the same whether or not there was anything to end: a caller
    /// asking to be signed out is entitled to be signed out, and telling them
    /// their token was already dead helps nobody and says something.
    /// </remarks>
    /// <param name="erneuerungstoken">What the caller presented, if anything.</param>
    /// <param name="cancellationToken">Cancels the attempt.</param>
    public async Task HandleAsync(
        string? erneuerungstoken,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(erneuerungstoken))
        {
            await sitzungen.EndByRefreshTokenAsync(erneuerungstoken, cancellationToken);
        }
    }
}
