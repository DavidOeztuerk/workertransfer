using Microsoft.EntityFrameworkCore;
using Npgsql;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using WorkerTransfer.Profile.Domain.Pruefspur;

namespace WorkerTransfer.Profile.Infrastructure.Persistence;

/// <summary>Baut Datenquelle und Kontext.</summary>
/// <remarks>
/// Die eine Stelle, an der die Verfolgung abgeschaltet und die Aufzählung
/// erklärt wird — damit Container, Tests und <c>dotnet ef</c> nicht mit drei
/// verschiedenen Modellen enden.
/// </remarks>
public static class ProfileDbContextFactory
{
    /// <summary>Die Postgres-Aufzählung hinter <c>audit_events.action</c>.</summary>
    public const string HandlungsTyp = "pruef_handlung";

    /// <summary>Eine Datenquelle, die die Aufzählung kennt.</summary>
    /// <remarks>
    /// Sie wird geschrieben, nicht nur gelesen, und Postgres nimmt in einer
    /// Aufzählungsspalte keinen Text an — der Parameter muss den Typ tragen.
    /// </remarks>
    /// <param name="connectionString">Wo die Profildatenbank liegt.</param>
    public static NpgsqlDataSource Datenquelle(string connectionString)
    {
        var erbauer = new NpgsqlDataSourceBuilder(connectionString);
        erbauer.MapEnum<Pruefhandlung>(HandlungsTyp);
        return erbauer.Build();
    }

    /// <summary>Einstellungen für eine Datenquelle, die dem Aufrufer gehört.</summary>
    /// <param name="options">Der zu füllende Erbauer.</param>
    /// <param name="datenquelle">Wo die Daten liegen.</param>
    public static DbContextOptionsBuilder Konfiguriere(
        DbContextOptionsBuilder options,
        NpgsqlDataSource datenquelle)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options
            .UseNpgsql(datenquelle, Gemeinsam)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
    }

    /// <summary>Dasselbe Modell für <c>dotnet ef</c>, das keine Verbindung braucht.</summary>
    /// <remarks>
    /// Muss genau das einstellen, was <see cref="Konfiguriere"/> einstellt. Ein
    /// Entwurfszeitmodell, das vom laufenden abweicht, erzeugt Migrationen für
    /// Änderungen, die niemand gemacht hat — und, schlimmer, keine für die, die
    /// jemand gemacht hat.
    /// </remarks>
    /// <param name="options">Der zu füllende Erbauer.</param>
    /// <param name="connectionString">Eine Zeichenkette, die nie geöffnet wird.</param>
    public static DbContextOptionsBuilder ZurEntwurfszeit(
        DbContextOptionsBuilder options,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.UseNpgsql(connectionString, Gemeinsam);
    }

    /// <summary>
    /// Beide Hälften der Aufzählung werden gebraucht: die Datenquelle bringt
    /// Npgsql den Typ bei, das hier bringt EF bei, dass die Spalte er ist.
    /// </summary>
    private static void Gemeinsam(NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.MapEnum<Pruefhandlung>(HandlungsTyp);
}

/// <summary>Baut einen Kontext für <c>dotnet ef</c>.</summary>
/// <remarks>
/// Hier statt im API-Projekt: eine Migration gehört der Schicht, die das Schema
/// besitzt, und sie zu erzeugen darf nicht verlangen, dass der Dienst
/// eingerichtet ist.
/// </remarks>
public sealed class ProfileEntwurfszeitFabrik
    : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<ProfileDbContext>
{
    /// <inheritdoc />
    public ProfileDbContext CreateDbContext(string[] args) =>
        new((DbContextOptions<ProfileDbContext>)ProfileDbContextFactory
            .ZurEntwurfszeit(
                new DbContextOptionsBuilder<ProfileDbContext>(),
                "Host=entwurfszeit;Database=profile")
            .Options);
}
