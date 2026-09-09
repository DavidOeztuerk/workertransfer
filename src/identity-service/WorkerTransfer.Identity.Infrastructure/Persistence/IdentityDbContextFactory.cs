using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using Npgsql;
using WorkerTransfer.Identity.Domain.Audit;
using WorkerTransfer.Identity.Domain.Users;

namespace WorkerTransfer.Identity.Infrastructure.Persistence;

/// <summary>
/// Builds the data source and the context that read the existing schema.
/// </summary>
public static class IdentityDbContextFactory
{
    /// <summary>The Postgres enum behind <c>audit_events.action</c>.</summary>
    private const string AuditActionTyp = "audit_action";

    /// <summary>The Postgres enum behind <c>users.status</c>.</summary>
    private const string AccountStatusTyp = "account_status";

    /// <summary>A data source that knows both Postgres enums.</summary>
    /// <remarks>
    /// Both are written, not only read, and Postgres accepts no text in an enum
    /// column — a parameter has to carry the type. <c>EnableUnmappedTypes</c>
    /// stays for everything else the Alembic schema holds that this service
    /// only reads.
    /// </remarks>
    public static NpgsqlDataSource DataSource(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.EnableUnmappedTypes();
        builder.MapEnum<AuditAction>(AuditActionTyp);
        builder.MapEnum<AccountStatus>(AccountStatusTyp);
        return builder.Build();
    }

    /// <summary>A context over <paramref name="connectionString"/>.</summary>
    public static IdentityDbContext Fuer(string connectionString) =>
        new(Optionen(DataSource(connectionString)).Options);

    /// <summary>Options for a data source the caller owns.</summary>
    public static DbContextOptionsBuilder<IdentityDbContext> Optionen(NpgsqlDataSource dataSource) =>
        (DbContextOptionsBuilder<IdentityDbContext>)Konfiguriere(
            new DbContextOptionsBuilder<IdentityDbContext>(), dataSource);

    /// <summary>
    /// The one place tracking is switched off and the enum is declared, so the
    /// container, the tests and <c>dotnet ef</c> cannot end up with different
    /// models.
    /// </summary>
    /// <param name="options">The builder to configure.</param>
    /// <param name="dataSource">Where the data lives.</param>
    public static DbContextOptionsBuilder Konfiguriere(
        DbContextOptionsBuilder options,
        NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.UseNpgsql(dataSource, Gemeinsam).UseQueryTrackingBehavior(
            QueryTrackingBehavior.NoTracking);
    }

    /// <summary>
    /// The same model, for <c>dotnet ef</c>, which needs no connection.
    /// </summary>
    /// <remarks>
    /// Must configure exactly what <see cref="Konfiguriere"/> configures. A
    /// design-time model that differs from the running one produces migrations
    /// for changes nobody made — and, worse, none for changes somebody did.
    /// </remarks>
    public static DbContextOptionsBuilder ZurEntwurfszeit(
        DbContextOptionsBuilder options,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.UseNpgsql(connectionString, Gemeinsam);
    }

    /// <summary>
    /// Both halves of an enum are needed. The data source teaches Npgsql the
    /// type; this teaches EF that the column is it — without it the parameter
    /// goes out as an integer and Postgres refuses it.
    /// </summary>
    private static void Gemeinsam(NpgsqlDbContextOptionsBuilder npgsql)
    {
        npgsql.MapEnum<AuditAction>(AuditActionTyp);
        npgsql.MapEnum<AccountStatus>(AccountStatusTyp);
    }
}
