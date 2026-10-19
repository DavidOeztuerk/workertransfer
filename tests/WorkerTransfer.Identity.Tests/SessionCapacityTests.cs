using FluentAssertions;
using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Identity.Infrastructure.Persistence;

namespace WorkerTransfer.Identity.Tests;

/// <summary>
/// What a sign-in acts as, across the table that remembers it.
/// </summary>
[Collection(PostgresCollection.Name)]
public class SessionCapacityTests(Postgres postgres) : IAsyncLifetime
{
    private readonly SessionId _session = SessionId.New();

    public async Task InitializeAsync()
    {
        await using var context = Context();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private IdentityDbContext Context() =>
        IdentityDbContextFactory.For(postgres.ConnectionString);

    /// <summary>
    /// Acting for oneself is the state a person is in. A missing row is the
    /// answer, not a gap — which is why signing in writes nothing here.
    /// </summary>
    [Fact]
    public async Task An_unknown_session_acts_for_itself()
    {
        await using var context = Context();

        var capacity = await new EfSessionCapacity(context).RecallAsync(SessionId.New());

        capacity.Should().BeOfType<Capacity.AsSelf>();
    }

    [Fact]
    public async Task A_remembered_company_comes_back_out()
    {
        var company = TenantId.New();

        await using (var context = Context())
        {
            await new EfSessionCapacity(context)
                .RememberAsync(_session, new Capacity.ForCompany(company));
            await context.SaveChangesAsync();
        }

        await using var read = Context();
        var capacity = await new EfSessionCapacity(read).RecallAsync(_session);

        capacity.Should().BeOfType<Capacity.ForCompany>().Which.Tenant.Should().Be(company);
    }

    /// <summary>
    /// The path a refresh takes when the membership is gone: back to the person,
    /// and nothing left behind that says otherwise.
    /// </summary>
    [Fact]
    public async Task Going_back_to_a_private_person_clears_the_row_away()
    {
        await using (var context = Context())
        {
            var capacity = new EfSessionCapacity(context);
            await capacity.RememberAsync(_session, new Capacity.ForCompany(TenantId.New()));
            await context.SaveChangesAsync();
        }

        await using (var context = Context())
        {
            await new EfSessionCapacity(context)
                .RememberAsync(_session, Capacity.AsSelf.Instance);
            await context.SaveChangesAsync();
        }

        await using var read = Context();
        (await read.SessionCapacities.CountAsync(row => row.SessionId == _session.Value))
            .Should().Be(0);
        (await new EfSessionCapacity(read).RecallAsync(_session))
            .Should().BeOfType<Capacity.AsSelf>();
    }

    /// <summary>
    /// A sign-in is remembered once and refreshed many times. Writing a second
    /// row per rotation would turn a table of sign-ins into a table of tokens.
    /// </summary>
    [Fact]
    public async Task The_same_session_twice_gives_one_row()
    {
        var first = TenantId.New();
        var after = TenantId.New();

        await using (var context = Context())
        {
            await new EfSessionCapacity(context)
                .RememberAsync(_session, new Capacity.ForCompany(first));
            await context.SaveChangesAsync();
        }

        await using (var context = Context())
        {
            await new EfSessionCapacity(context)
                .RememberAsync(_session, new Capacity.ForCompany(after));
            await context.SaveChangesAsync();
        }

        await using var read = Context();
        (await read.SessionCapacities.CountAsync(row => row.SessionId == _session.Value))
            .Should().Be(1);
        (await new EfSessionCapacity(read).RecallAsync(_session))
            .Should().BeOfType<Capacity.ForCompany>().Which.Tenant.Should().Be(after);
    }

    [Fact]
    public async Task Forgetting_is_no_error_even_without_a_row()
    {
        await using var context = Context();

        var forgotten = async () =>
        {
            await new EfSessionCapacity(context).ForgetAsync(SessionId.New());
            await context.SaveChangesAsync();
        };

        await forgotten.Should().NotThrowAsync();
    }
}
