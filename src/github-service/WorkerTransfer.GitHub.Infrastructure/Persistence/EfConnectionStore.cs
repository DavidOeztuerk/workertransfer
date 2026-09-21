using System.Text.Json;
using System.Text.Json.Serialization;
using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.GitHub.Domain.Connections;

namespace WorkerTransfer.GitHub.Infrastructure.Persistence;

/// <summary>A repository as it stands in the jsonb column.</summary>
/// <remarks>
/// Names of its own for the column rather than the aggregate's: what lies here
/// is a copy of what GitHub said, and whoever reads the row by hand later
/// should recognise the fields.
/// </remarks>
internal sealed record RepositoryDocument(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("language")] string? Language,
    [property: JsonPropertyName("stars")] int Stars,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("pushed_at")] DateTimeOffset? PushedAt,
    // Added later: older rows do not have the two fields, and `null` becomes
    // an empty list on reading. No follow-up migration is needed for it — at
    // the next fetch they stand there.
    [property: JsonPropertyName("languages")] IReadOnlyList<string>? Languages = null,
    [property: JsonPropertyName("topics")] IReadOnlyList<string>? Topics = null);

/// <summary>Reads and writes <c>github_connections</c>.</summary>
public sealed class EfConnectionStore(GitHubDbContext context) : IConnectionStore
{
    /// <inheritdoc />
    public async Task<Connection?> GetAsync(
        SubjectId subject, CancellationToken cancellationToken = default)
    {
        var row = await context.Connections
            .FirstOrDefaultAsync(candidate => candidate.Id == subject.Value, cancellationToken);

        return row is null ? null : ToAggregate(row);
    }

    /// <inheritdoc />
    public async Task SaveAsync(
        Connection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var existing = await context.Connections
            .AsTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.Id == connection.Subject.Value, cancellationToken);

        if (existing is null)
        {
            var row = new ConnectionRow
            {
                Id = connection.Subject.Value,
                CreatedAt = DateTime.UtcNow
            };

            Transfer(connection, row);
            context.Connections.Add(row);

            return;
        }

        Transfer(connection, existing);
    }

    /// <inheritdoc />
    public Task DeleteAsync(SubjectId subject, CancellationToken cancellationToken = default) =>
        context.Connections
            .Where(row => row.Id == subject.Value)
            .ExecuteDeleteAsync(cancellationToken);

    private static void Transfer(Connection connection, ConnectionRow row)
    {
        row.Login = connection.Login;
        row.Challenge = connection.Nonce;
        row.VerifiedAt = connection.VerifiedAt?.UtcDateTime;
        row.FetchedAt = connection.FetchedAt?.UtcDateTime;
        row.LanguagesComplete = connection.LanguagesComplete;
        row.Repositories = JsonSerializer.Serialize(
            connection.Repositories.Select(entry => new RepositoryDocument(
                entry.Name, entry.Description, entry.Language,
                entry.Stars, entry.Url, entry.LastPushedAt,
                entry.Languages, entry.Topics)));
        row.UpdatedAt = DateTime.UtcNow;
    }

    private static Connection ToAggregate(ConnectionRow row)
    {
        var abgelegt = JsonSerializer.Deserialize<List<RepositoryDocument>>(row.Repositories) ?? [];

        return Connection.Restore(
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
