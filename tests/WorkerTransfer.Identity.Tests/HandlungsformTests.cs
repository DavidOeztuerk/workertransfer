using FluentAssertions;
using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Identity.Infrastructure.Persistence;

namespace WorkerTransfer.Identity.Tests;

/// <summary>
/// What a sign-in acts as, across the table that remembers it.
/// </summary>
[Collection(PostgresCollection.Name)]
public class HandlungsformTests(Postgres postgres) : IAsyncLifetime
{
    private readonly SessionId _sitzung = SessionId.New();

    public async Task InitializeAsync()
    {
        await using var context = Kontext();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private IdentityDbContext Kontext() =>
        IdentityDbContextFactory.Fuer(postgres.ConnectionString);

    /// <summary>
    /// Acting for oneself is the state a person is in. A missing row is the
    /// answer, not a gap — which is why signing in writes nothing here.
    /// </summary>
    [Fact]
    public async Task Eine_unbekannte_Sitzung_handelt_fuer_sich_selbst()
    {
        await using var context = Kontext();

        var form = await new EfSessionCapacity(context).RecallAsync(SessionId.New());

        form.Should().BeOfType<Capacity.AsSelf>();
    }

    [Fact]
    public async Task Eine_gemerkte_Firma_kommt_wieder_heraus()
    {
        var company = TenantId.New();

        await using (var context = Kontext())
        {
            await new EfSessionCapacity(context)
                .RememberAsync(_sitzung, new Capacity.ForCompany(company));
            await context.SaveChangesAsync();
        }

        await using var gelesen = Kontext();
        var form = await new EfSessionCapacity(gelesen).RecallAsync(_sitzung);

        form.Should().BeOfType<Capacity.ForCompany>().Which.Tenant.Should().Be(company);
    }

    /// <summary>
    /// The path a refresh takes when the membership is gone: back to the person,
    /// and nothing left behind that says otherwise.
    /// </summary>
    [Fact]
    public async Task Zurueck_zur_Privatperson_raeumt_die_Zeile_weg()
    {
        await using (var context = Kontext())
        {
            var form = new EfSessionCapacity(context);
            await form.RememberAsync(_sitzung, new Capacity.ForCompany(TenantId.New()));
            await context.SaveChangesAsync();
        }

        await using (var context = Kontext())
        {
            await new EfSessionCapacity(context)
                .RememberAsync(_sitzung, Capacity.AsSelf.Instance);
            await context.SaveChangesAsync();
        }

        await using var gelesen = Kontext();
        (await gelesen.SessionCapacities.CountAsync(row => row.SessionId == _sitzung.Value))
            .Should().Be(0);
        (await new EfSessionCapacity(gelesen).RecallAsync(_sitzung))
            .Should().BeOfType<Capacity.AsSelf>();
    }

    /// <summary>
    /// A sign-in is remembered once and refreshed many times. Writing a second
    /// row per rotation would turn a table of sign-ins into a table of tokens.
    /// </summary>
    [Fact]
    public async Task Zweimal_dieselbe_Sitzung_ergibt_eine_Zeile()
    {
        var zuerst = TenantId.New();
        var danach = TenantId.New();

        await using (var context = Kontext())
        {
            await new EfSessionCapacity(context)
                .RememberAsync(_sitzung, new Capacity.ForCompany(zuerst));
            await context.SaveChangesAsync();
        }

        await using (var context = Kontext())
        {
            await new EfSessionCapacity(context)
                .RememberAsync(_sitzung, new Capacity.ForCompany(danach));
            await context.SaveChangesAsync();
        }

        await using var gelesen = Kontext();
        (await gelesen.SessionCapacities.CountAsync(row => row.SessionId == _sitzung.Value))
            .Should().Be(1);
        (await new EfSessionCapacity(gelesen).RecallAsync(_sitzung))
            .Should().BeOfType<Capacity.ForCompany>().Which.Tenant.Should().Be(danach);
    }

    [Fact]
    public async Task Vergessen_ist_auch_ohne_Zeile_kein_Fehler()
    {
        await using var context = Kontext();

        var vergessen = async () =>
        {
            await new EfSessionCapacity(context).ForgetAsync(SessionId.New());
            await context.SaveChangesAsync();
        };

        await vergessen.Should().NotThrowAsync();
    }
}
