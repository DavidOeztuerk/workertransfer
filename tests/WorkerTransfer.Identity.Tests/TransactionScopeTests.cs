using FluentAssertions;
using Noelia.Abstractions.Security.Sessions;
using Noelia.Core.Identity;
using Noelia.Data.EntityFrameworkCore;
using Noelia.Data.EntityFrameworkCore.Sessions;
using Noelia.Infrastructure.Security.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace WorkerTransfer.Identity.Tests;

/// <summary>A context that holds nothing but Noelia's own table.</summary>
public sealed class NoeliaOnlyContext(DbContextOptions<NoeliaOnlyContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        // Its own name, so this probe cannot collide with the table the
        // service's own migration creates in the same database.
        modelBuilder.ConfigureNoeliaRefreshTokens("probe_refresh_tokens");
    }
}

/// <summary>
/// Whether Noelia's session store can run inside a transaction the application
/// opened.
/// </summary>
/// <remarks>
/// The audit trail has to commit together with the change it records, which
/// means one transaction per command. Noelia's refresh-token store manages its
/// own atomicity and opens a transaction of its own while consuming a token —
/// so whether the two compose decides how the sign-in commands are cut.
/// <para>
/// Deliberately built out of Noelia alone. It began as the reproduction for a
/// Noelia bug — the store opened a second transaction and threw
/// <c>"The connection is already in a transaction"</c> — fixed in 3.0.1. The
/// ticket is gone; the test stays, because what it pins is ours: that the two
/// sign-in routes really do commit their audit row together with the change.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public class TransactionScopeTests(Postgres postgres) : IAsyncLifetime
{
    private ServiceProvider _provider = null!;

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.Configure<TokenSessionOptions>(_ => { });
        services.AddDbContext<NoeliaOnlyContext>(
            options => options.UseNpgsql(postgres.ConnectionString));
        services.AddEntityFrameworkRefreshTokens<NoeliaOnlyContext>();

        // Since 5.1.0 `SessionObservations` is a singleton and the fifth
        // constructor parameter of `TokenSessionService` (MIGRATION.md:451).
        // Before, the set of observed subjects was an instance field on an
        // `AddScoped` service — a new, empty dictionary per request, which is
        // why the session overview permanently reported "0 active sessions".
        //
        // This suite builds the service itself instead of running the module; it
        // therefore has to supply the singleton itself. There is deliberately no
        // overload with four parameters — it would create a set of its own and
        // thereby silently restore exactly that defect.
        services.AddSingleton<SessionObservations>();
        services.AddScoped<ITokenSessionService, TokenSessionService>();

        _provider = services.BuildServiceProvider();

        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NoeliaOnlyContext>();

        // Not EnsureCreated: the database already carries the Alembic schema,
        // so it would decide there is nothing to do. xunit builds one
        // instance per test, hence the check.
        var existing = await context.Database
            .SqlQuery<int>($"SELECT 1 AS \"Value\" FROM pg_class WHERE relname = 'probe_refresh_tokens'")
            .AnyAsync();

        if (!existing)
        {
            await ((RelationalDatabaseCreator)context.Database.GetService<IDatabaseCreator>())
                .CreateTablesAsync();
        }
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    /// <summary>
    /// Starting a sign-in composes: <c>CreateAsync</c> only saves, and a save
    /// joins whatever transaction is open.
    /// </summary>
    [Fact]
    public async Task A_sign_in_runs_inside_an_open_transaction()
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NoeliaOnlyContext>();
        var sessions = scope.ServiceProvider.GetRequiredService<ITokenSessionService>();

        await using var transaction = await context.Database.BeginTransactionAsync();

        var signedIn = await sessions.SignInAsync(SubjectId.New());
        await transaction.CommitAsync();

        signedIn.RefreshToken.Should().NotBeEmpty();
    }

    /// <summary>
    /// And a refresh likewise: it joins the open transaction instead of opening
    /// a second one, and the rotation becomes permanent together with the
    /// caller's work.
    /// </summary>
    /// <remarks>
    /// Until Girder 3.0.1 this place threw
    /// <c>"The connection is already in a transaction"</c> — the reason
    /// <c>POST /auth/refresh</c> and <c>/auth/logout</c> stood red for a while.
    /// </remarks>
    [Fact]
    public async Task A_refresh_runs_inside_an_open_transaction()
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NoeliaOnlyContext>();
        var sessions = scope.ServiceProvider.GetRequiredService<ITokenSessionService>();

        var signedIn = await sessions.SignInAsync(SubjectId.New());

        await using var transaction = await context.Database.BeginTransactionAsync();
        var refreshed = await sessions.RefreshAsync(signedIn.RefreshToken);
        await transaction.CommitAsync();

        refreshed.Succeeded.Should().BeTrue();
        refreshed.RefreshToken.Should().NotBeNullOrEmpty()
            .And.NotBe(signedIn.RefreshToken, "a refresh rotates the token");

        // After the caller's commit: the old token really is spent. Presenting
        // it again now takes the branch for an already rotated row — inside the
        // grace window a sibling, then, not a theft. That proves the rotation
        // hung on the caller's transaction and survived its commit; without it
        // Rotated would stand here.
        var onceMore = await sessions.RefreshAsync(signedIn.RefreshToken);
        onceMore.Outcome.Should().Be(ConsumeOutcome.RotatedWithinGrace);
    }

    /// <summary>
    /// And a sign-out likewise — the second route out of the same ticket.
    /// </summary>
    /// <remarks>
    /// <c>POST /auth/logout</c> is composite: first spend the token in order to
    /// know the session, then close the session. Both steps lie in the caller's
    /// transaction, because the audit-trail row <c>token_revoke</c> falls beside
    /// them — and an entry that commits without the sign-out asserts something
    /// that did not happen.
    /// <para>
    /// Until Girder 3.0.1 the same fault stood here as one line above. The test
    /// stands here in its own right all the same: the refresh covers only the
    /// first of the two steps, and <c>SignOutAsync</c> is a different call.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_sign_out_runs_inside_an_open_transaction()
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<NoeliaOnlyContext>();
        var sessions = scope.ServiceProvider.GetRequiredService<ITokenSessionService>();

        var signedIn = await sessions.SignInAsync(SubjectId.New());

        await using var transaction = await context.Database.BeginTransactionAsync();

        var consumedAt = await sessions.RefreshAsync(signedIn.RefreshToken);

        if (consumedAt is not { Session: { } session })
        {
            throw new InvalidOperationException(
                "the refresh named no session — the test then checks nothing");
        }

        await sessions.SignOutAsync(session);
        await transaction.CommitAsync();

        // After the caller's commit the session really is closed. Without the
        // sign-out the just-rotated token would still carry the grace window and
        // would refresh — which is why it is exactly the probe.
        var after = await sessions.RefreshAsync(consumedAt.RefreshToken!);
        after.Succeeded.Should().BeFalse(
            "the sign-out lay in the same transaction and survived its commit");
    }
}
