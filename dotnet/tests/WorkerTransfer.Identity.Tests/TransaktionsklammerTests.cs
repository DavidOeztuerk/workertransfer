using FluentAssertions;
using Girder.Core.Identity;
using Girder.Data.EntityFrameworkCore;
using Girder.Data.EntityFrameworkCore.Sessions;
using Girder.Infrastructure.Security.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace WorkerTransfer.Identity.Tests;

/// <summary>A context that holds nothing but Girder's own table.</summary>
public sealed class NurGirderKontext(DbContextOptions<NurGirderKontext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        // Its own name, so this probe cannot collide with the table the
        // service's own migration creates in the same database.
        modelBuilder.ConfigureGirderRefreshTokens("probe_refresh_tokens");
    }
}

/// <summary>
/// Whether Girder's session store can run inside a transaction the application
/// opened.
/// </summary>
/// <remarks>
/// The audit trail has to commit together with the change it records, which
/// means one transaction per command. Girder's refresh-token store manages its
/// own atomicity and opens a transaction of its own while consuming a token —
/// so whether the two compose decides how the sign-in commands are cut.
/// <para>
/// Deliberately built out of Girder alone: it is the reproduction for
/// <c>bugs/sitzungsspeicher-vertraegt-keine-aeussere-transaktion.md</c>, and a
/// reproduction that needs WorkerTransfer code proves nothing about whose the
/// error is.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public class TransaktionsklammerTests(Postgres postgres) : IAsyncLifetime
{
    private ServiceProvider _anbieter = null!;

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.Configure<TokenSessionOptions>(_ => { });
        services.AddDbContext<NurGirderKontext>(
            options => options.UseNpgsql(postgres.ConnectionString));
        services.AddEntityFrameworkRefreshTokens<NurGirderKontext>();
        services.AddScoped<ITokenSessionService, TokenSessionService>();

        _anbieter = services.BuildServiceProvider();

        using var bereich = _anbieter.CreateScope();
        var kontext = bereich.ServiceProvider.GetRequiredService<NurGirderKontext>();

        // Not EnsureCreated: the database already carries the Alembic schema,
        // so it would decide there is nothing to do. xunit builds one
        // instance per test, hence the check.
        var vorhanden = await kontext.Database
            .SqlQuery<int>($"SELECT 1 AS \"Value\" FROM pg_class WHERE relname = 'probe_refresh_tokens'")
            .AnyAsync();

        if (!vorhanden)
        {
            await ((RelationalDatabaseCreator)kontext.Database.GetService<IDatabaseCreator>())
                .CreateTablesAsync();
        }
    }

    public async Task DisposeAsync() => await _anbieter.DisposeAsync();

    /// <summary>
    /// Starting a sign-in composes: <c>CreateAsync</c> only saves, and a save
    /// joins whatever transaction is open.
    /// </summary>
    [Fact]
    public async Task Eine_Anmeldung_laeuft_in_einer_offenen_Transaktion()
    {
        using var bereich = _anbieter.CreateScope();
        var kontext = bereich.ServiceProvider.GetRequiredService<NurGirderKontext>();
        var sitzungen = bereich.ServiceProvider.GetRequiredService<ITokenSessionService>();

        await using var klammer = await kontext.Database.BeginTransactionAsync();

        var angemeldet = await sitzungen.SignInAsync(SubjectId.New());
        await klammer.CommitAsync();

        angemeldet.RefreshToken.Should().NotBeEmpty();
    }

    /// <summary>
    /// A refresh does not: <c>TryConsumeAsync</c> opens a transaction of its
    /// own, and the connection already has one.
    /// </summary>
    [Fact]
    public async Task Eine_Erneuerung_laeuft_nicht_in_einer_offenen_Transaktion()
    {
        using var bereich = _anbieter.CreateScope();
        var kontext = bereich.ServiceProvider.GetRequiredService<NurGirderKontext>();
        var sitzungen = bereich.ServiceProvider.GetRequiredService<ITokenSessionService>();

        var angemeldet = await sitzungen.SignInAsync(SubjectId.New());

        await using var klammer = await kontext.Database.BeginTransactionAsync();

        var erneuern = async () => await sitzungen.RefreshAsync(angemeldet.RefreshToken);

        (await erneuern.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*already in a transaction*");
    }
}
