using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using WorkerTransfer.Profile.Domain.Audit;

namespace WorkerTransfer.Profile.Infrastructure.Persistence;

/// <summary>Builds data source and context.</summary>
/// <remarks>
/// The one place where tracking is switched off and the enum is declared — so
/// that container, tests and <c>dotnet ef</c> do not end up with three
/// different models.
/// </remarks>
public static class ProfileDbContextFactory
{
    /// <summary>The Postgres enum behind <c>audit_events.action</c>.</summary>
    /// <remarks>
    /// The value is schema: the migration created the type under this name.
    /// The members of <see cref="AuditAction"/> are its labels.
    /// </remarks>
    public const string AuditActionType = "audit_action";

    /// <summary>A data source that knows the enum.</summary>
    /// <remarks>
    /// It is written, not only read, and Postgres accepts no text in an enum
    /// column — the parameter has to carry the type.
    /// </remarks>
    /// <param name="connectionString">Where the profile database lies.</param>
    public static NpgsqlDataSource DataSource(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.MapEnum<AuditAction>(AuditActionType);
        return builder.Build();
    }

    /// <summary>Options for a data source that belongs to the caller.</summary>
    /// <param name="options">The builder to fill.</param>
    /// <param name="dataSource">Where the data lies.</param>
    public static DbContextOptionsBuilder Configure(
        DbContextOptionsBuilder options,
        NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options
            .UseNpgsql(dataSource, Shared)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
    }

    /// <summary>The same model for <c>dotnet ef</c>, which needs no connection.</summary>
    /// <remarks>
    /// Must set exactly what <see cref="Configure"/> sets. A design-time model
    /// that differs from the running one produces migrations for changes nobody
    /// made — and, worse, none for those somebody made.
    /// </remarks>
    /// <param name="options">The builder to fill.</param>
    /// <param name="connectionString">A string that is never opened.</param>
    public static DbContextOptionsBuilder AtDesignTime(
        DbContextOptionsBuilder options,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.UseNpgsql(connectionString, Shared);
    }

    /// <summary>
    /// Both halves of the enum are needed: the data source teaches Npgsql the
    /// type, this teaches EF that the column is it.
    /// </summary>
    private static void Shared(NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.MapEnum<AuditAction>(AuditActionType);
}

/// <summary>Builds a context for <c>dotnet ef</c>.</summary>
/// <remarks>
/// Here instead of in the API project: a migration belongs to the layer that
/// owns the schema, and generating it must not require the service to be set
/// up.
/// </remarks>
public sealed class ProfileDesignTimeFactory
    : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<ProfileDbContext>
{
    /// <inheritdoc />
    public ProfileDbContext CreateDbContext(string[] args) =>
        new((DbContextOptions<ProfileDbContext>)ProfileDbContextFactory
            .AtDesignTime(
                new DbContextOptionsBuilder<ProfileDbContext>(),
                "Host=design-time;Database=profile")
            .Options);
}
