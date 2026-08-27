using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using WorkerTransfer.Resume.Domain.Anfragen;
using WorkerTransfer.Resume.Domain.Pruefspur;

namespace WorkerTransfer.Resume.Infrastructure.Persistence;

/// <summary>Builds the data source and the context over this service's schema.</summary>
public static class ResumeDbContextFactory
{
    /// <summary>A data source that knows both Postgres enums.</summary>
    /// <remarks>
    /// Both are written as well as read, and Postgres takes no text in an enum
    /// column — a parameter has to carry the type.
    /// </remarks>
    public static NpgsqlDataSource DataSource(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.MapEnum<Anfragestand>(Enumnamen.Anfragestand);
        builder.MapEnum<Pruefhandlung>(Enumnamen.Pruefhandlung);
        return builder.Build();
    }

    /// <summary>Options for a data source the caller owns.</summary>
    public static DbContextOptionsBuilder<ResumeDbContext> Optionen(NpgsqlDataSource dataSource) =>
        (DbContextOptionsBuilder<ResumeDbContext>)Konfiguriere(
            new DbContextOptionsBuilder<ResumeDbContext>(), dataSource);

    /// <summary>
    /// The one place tracking is switched off and the enums are declared, so
    /// the container, the tests and <c>dotnet ef</c> cannot end up with
    /// different models.
    /// </summary>
    public static DbContextOptionsBuilder Konfiguriere(
        DbContextOptionsBuilder options,
        NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options
            .UseNpgsql(dataSource, Gemeinsam)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
    }

    /// <summary>The same model for <c>dotnet ef</c>, which needs no connection.</summary>
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
    /// Both halves of an enum are needed: the data source teaches Npgsql the
    /// type, this teaches EF that the column is it. Without the second the
    /// parameter goes out as an integer and Postgres refuses it.
    /// </summary>
    private static void Gemeinsam(NpgsqlDbContextOptionsBuilder npgsql)
    {
        npgsql.MapEnum<Anfragestand>(Enumnamen.Anfragestand);
        npgsql.MapEnum<Pruefhandlung>(Enumnamen.Pruefhandlung);
    }
}
