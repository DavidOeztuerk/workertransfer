using FluentAssertions;
using Girder.Core.Identity;
using Npgsql;
using WorkerTransfer.Identity.Domain.Users;
using WorkerTransfer.Identity.Infrastructure.Persistence;

namespace WorkerTransfer.Identity.Tests;

/// <summary>
/// Reading the <c>users</c> table Python owns.
/// </summary>
/// <remarks>
/// The rows are written the way the Python service writes them — a Postgres
/// enum for the status, citext for the address, jsonb for the roles — and read
/// back through the repository this service uses.
/// </remarks>
[Collection(PostgresCollection.Name)]
public class BenutzerAbbildungTests(Postgres postgres)
{
    private IdentityDbContext Kontext() => IdentityDbContextFactory.Fuer(postgres.ConnectionString);

    private async Task<Guid> SchreibeBenutzer(
        string email,
        string status = "active",
        string hash = "$2b$12$K7ZQe8YHRy9zJQx1uKcOxeVJ0N4tGZ0dQpXWq6yLNfHb3sRmCvA1a")
    {
        var id = Guid.NewGuid();
        await using var verbindung = new NpgsqlConnection(postgres.ConnectionString);
        await verbindung.OpenAsync();

        await using var befehl = verbindung.CreateCommand();
        befehl.CommandText = """
            INSERT INTO users (id, email, password_hash, display_name, status, roles,
                               created_at, updated_at, version)
            VALUES (@id, @email, @hash, @name, @status::account_status, @roles::jsonb,
                    now(), now(), 1)
            """;
        befehl.Parameters.AddWithValue("id", id);
        befehl.Parameters.AddWithValue("email", email);
        befehl.Parameters.AddWithValue("hash", hash);
        befehl.Parameters.AddWithValue("name", "Anna");
        befehl.Parameters.AddWithValue("status", status);
        befehl.Parameters.AddWithValue("roles", """["user"]""");
        await befehl.ExecuteNonQueryAsync();

        return id;
    }

    [Fact]
    public async Task Eine_Zeile_die_Python_geschrieben_hat_wird_zum_Aggregat()
    {
        var id = await SchreibeBenutzer("anna@example.com");

        var benutzer = await new EfUserRepository(Kontext()).FindByEmailAsync("anna@example.com");

        benutzer.Should().NotBeNull();
        benutzer!.Id.Should().Be(new SubjectId(id));
        benutzer.Email.Should().Be("anna@example.com");
        benutzer.DisplayName.Should().Be("Anna");
        benutzer.Status.Should().Be(AccountStatus.Active);
        benutzer.Roles.Should().Equal("user");
        benutzer.PasswordHash.Should().StartWith("$2b$12$");
    }

    /// <summary>
    /// <c>users.email</c> is citext, so the comparison is the database's and is
    /// case-insensitive. A client-side comparison would quietly turn one account
    /// into two.
    /// </summary>
    [Fact]
    public async Task Die_Adresse_wird_ohne_Ruecksicht_auf_Gross_und_Kleinschreibung_gefunden()
    {
        await SchreibeBenutzer("Berta@Example.COM");

        var benutzer = await new EfUserRepository(Kontext()).FindByEmailAsync("berta@example.com");

        benutzer.Should().NotBeNull();
    }

    [Fact]
    public async Task Jeder_Kontozustand_wird_gelesen_wie_er_dasteht()
    {
        await SchreibeBenutzer("clara@example.com", status: "pending");
        await SchreibeBenutzer("dora@example.com", status: "suspended");

        var repository = new EfUserRepository(Kontext());

        (await repository.FindByEmailAsync("clara@example.com"))!.Status
            .Should().Be(AccountStatus.Pending);
        (await repository.FindByEmailAsync("dora@example.com"))!.Status
            .Should().Be(AccountStatus.Suspended);
    }

    [Fact]
    public async Task Eine_unbekannte_Adresse_ergibt_null()
    {
        var benutzer = await new EfUserRepository(Kontext())
            .FindByEmailAsync("niemand@example.com");

        benutzer.Should().BeNull();
    }

    /// <summary>
    /// The aggregate is a different object than the row, as it is in Python.
    /// Nothing done to it can reach the database.
    /// </summary>
    [Fact]
    public async Task Das_Aggregat_haengt_nicht_am_Aenderungsverfolger()
    {
        await SchreibeBenutzer("emil@example.com");
        var kontext = Kontext();

        await new EfUserRepository(kontext).FindByEmailAsync("emil@example.com");

        kontext.ChangeTracker.Entries().Should().BeEmpty();
    }
}
