using System.Text.Json;
using System.Text.Json.Serialization;
using Girder.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.GitHub.Domain.Verbindungen;

namespace WorkerTransfer.GitHub.Infrastructure.Persistence;

/// <summary>Ein Repository, wie es in der jsonb-Spalte steht.</summary>
/// <remarks>
/// Eigene Namen für die Spalte statt der deutschen des Aggregats: was hier
/// liegt, ist eine Kopie dessen, was GitHub gesagt hat, und wer die Zeile
/// später von Hand liest, soll die Felder wiedererkennen.
/// </remarks>
internal sealed record RepoAblage(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("language")] string? Language,
    [property: JsonPropertyName("stars")] int Stars,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("pushed_at")] DateTimeOffset? PushedAt);

/// <summary>Liest und schreibt <c>github_connections</c>.</summary>
public sealed class EfVerbindungsspeicher(GitHubDbContext kontext) : IVerbindungsspeicher
{
    /// <inheritdoc />
    public async Task<Verbindung?> HoleAsync(
        SubjectId wer, CancellationToken cancellationToken = default)
    {
        var zeile = await kontext.Verbindungen
            .FirstOrDefaultAsync(kandidat => kandidat.Id == wer.Value, cancellationToken);

        return zeile is null ? null : ZumAggregat(zeile);
    }

    /// <inheritdoc />
    public async Task SichereAsync(
        Verbindung verbindung, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(verbindung);

        var vorhanden = await kontext.Verbindungen
            .AsTracking()
            .FirstOrDefaultAsync(
                kandidat => kandidat.Id == verbindung.Wer.Value, cancellationToken);

        if (vorhanden is null)
        {
            var zeile = new VerbindungsZeile
            {
                Id = verbindung.Wer.Value,
                CreatedAt = DateTime.UtcNow
            };

            Uebertrage(verbindung, zeile);
            kontext.Verbindungen.Add(zeile);

            return;
        }

        Uebertrage(verbindung, vorhanden);
    }

    /// <inheritdoc />
    public Task LoescheAsync(SubjectId wer, CancellationToken cancellationToken = default) =>
        kontext.Verbindungen
            .Where(zeile => zeile.Id == wer.Value)
            .ExecuteDeleteAsync(cancellationToken);

    private static void Uebertrage(Verbindung verbindung, VerbindungsZeile zeile)
    {
        zeile.Login = verbindung.Login;
        zeile.Challenge = verbindung.Einmalzeichenfolge;
        zeile.VerifiedAt = verbindung.NachgewiesenAm?.UtcDateTime;
        zeile.FetchedAt = verbindung.GeholtAm?.UtcDateTime;
        zeile.Repositories = JsonSerializer.Serialize(
            verbindung.Repositories.Select(eintrag => new RepoAblage(
                eintrag.Name, eintrag.Beschreibung, eintrag.Sprache,
                eintrag.Sterne, eintrag.Adresse, eintrag.ZuletztGeschoben)));
        zeile.UpdatedAt = DateTime.UtcNow;
    }

    private static Verbindung ZumAggregat(VerbindungsZeile zeile)
    {
        var abgelegt = JsonSerializer.Deserialize<List<RepoAblage>>(zeile.Repositories) ?? [];

        return Verbindung.Stelle_her(
            new SubjectId(zeile.Id),
            zeile.Login,
            zeile.Challenge,
            zeile.VerifiedAt is { } bewiesen
                ? new DateTimeOffset(bewiesen, TimeSpan.Zero)
                : null,
            zeile.FetchedAt is { } geholt ? new DateTimeOffset(geholt, TimeSpan.Zero) : null,
            [
                .. abgelegt.Select(eintrag => new Repository(
                    eintrag.Name, eintrag.Description, eintrag.Language,
                    eintrag.Stars, eintrag.Url, eintrag.PushedAt))
            ]);
    }
}
