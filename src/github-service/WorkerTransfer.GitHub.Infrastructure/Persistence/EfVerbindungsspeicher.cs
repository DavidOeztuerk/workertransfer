using System.Text.Json;
using System.Text.Json.Serialization;
using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.GitHub.Domain.Connections;

namespace WorkerTransfer.GitHub.Infrastructure.Persistence;

/// <summary>Ein Repository, wie es in der jsonb-Spalte steht.</summary>
/// <remarks>
/// Own Namen für die Spalte statt der deutschen des Aggregats: was hier
/// liegt, ist eine Kopie dessen, was GitHub gesagt hat, und wer die Zeile
/// später von Hand liest, soll die Felder wiedererkennen.
/// </remarks>
internal sealed record RepositoryDocumentStore(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("language")] string? Language,
    [property: JsonPropertyName("stars")] int Stars,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("pushed_at")] DateTimeOffset? PushedAt,
    // Nachträglich dazugekommen: ältere Zeilen haben die beiden Felder nicht,
    // und `null` wird beim Lesen zu einer leeren Liste. Eine Nachwanderung
    // braucht es dafür nicht — beim nächsten Abruf stehen sie da.
    [property: JsonPropertyName("languages")] IReadOnlyList<string>? Languages = null,
    [property: JsonPropertyName("topics")] IReadOnlyList<string>? Topics = null);

/// <summary>Liest und schreibt <c>github_connections</c>.</summary>
public sealed class EfConnectionStore(GitHubDbContext context) : IConnectionStore
{
    /// <inheritdoc />
    public async Task<Connection?> GetAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var row = await context.Connections
            .FirstOrDefaultAsync(kandidat => kandidat.Id == subject.Value, cancellationToken);

        return row is null ? null : ZumAggregat(row);
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        Connection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var vorhanden = await context.Connections
            .AsTracking()
            .FirstOrDefaultAsync(
                kandidat => kandidat.Id == connection.Wer.Value, cancellationToken);

        if (vorhanden is null)
        {
            var row = new ConnectionRow
            {
                Id = connection.Wer.Value,
                CreatedAt = DateTime.UtcNow
            };

            Uebertrage(connection, row);
            context.Connections.Add(row);

            return;
        }

        Uebertrage(connection, vorhanden);
    }

    /// <inheritdoc />
    public Task DeleteAsync(SubjectId subject, CancellationToken cancellationToken = default) =>
        context.Connections
            .Where(row => row.Id == subject.Value)
            .ExecuteDeleteAsync(cancellationToken);

    private static void Uebertrage(Connection connection, ConnectionRow row)
    {
        row.Login = connection.Login;
        row.Challenge = connection.Einmalzeichenfolge;
        row.VerifiedAt = connection.NachgewiesenAm?.UtcDateTime;
        row.FetchedAt = connection.GeholtAm?.UtcDateTime;
        row.LanguagesComplete = connection.SprachenVollstaendig;
        row.Repositories = JsonSerializer.Serialize(
            connection.Repositories.Select(entry => new RepositoryDocumentStore(
                entry.Name, entry.Beschreibung, entry.Sprache,
                entry.Sterne, entry.Adresse, entry.ZuletztGeschoben,
                entry.Sprachen, entry.Themen)));
        row.UpdatedAt = DateTime.UtcNow;
    }

    private static Connection ZumAggregat(ConnectionRow row)
    {
        var abgelegt = JsonSerializer.Deserialize<List<RepositoryDocumentStore>>(row.Repositories) ?? [];

        return Connection.Stelle_her(
            new SubjectId(row.Id),
            row.Login,
            row.Challenge,
            row.VerifiedAt is { } bewiesen
                ? new DateTimeOffset(bewiesen, TimeSpan.Zero)
                : null,
            row.FetchedAt is { } geholt ? new DateTimeOffset(geholt, TimeSpan.Zero) : null,
            [
                .. abgelegt.Select(entry => new Repository(
                    entry.Name, entry.Description, entry.Language,
                    entry.Stars, entry.Url, entry.PushedAt,
                    entry.Languages ?? [], entry.Topics ?? []))
            ],
            row.LanguagesComplete);
    }
}
