using FluentAssertions;
using Noelia.Core.Identity;
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
public class UserMappingTests(Postgres postgres)
{
    private IdentityDbContext Context() => IdentityDbContextFactory.For(postgres.ConnectionString);

    private async Task<Guid> WriteUser(
        string email,
        string status = "active",
        string hash = "$2b$12$K7ZQe8YHRy9zJQx1uKcOxeVJ0N4tGZ0dQpXWq6yLNfHb3sRmCvA1a")
    {
        var id = Guid.NewGuid();
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO users (id, email, password_hash, display_name, status, roles,
                               created_at, updated_at, version)
            VALUES (@id, @email, @hash, @name, @status::account_status, @roles::jsonb,
                    now(), now(), 1)
            """;
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("email", email);
        command.Parameters.AddWithValue("hash", hash);
        command.Parameters.AddWithValue("name", "Anna");
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("roles", """["user"]""");
        await command.ExecuteNonQueryAsync();

        return id;
    }

    [Fact]
    public async Task A_row_Python_wrote_becomes_the_aggregate()
    {
        var id = await WriteUser("anna@example.com");

        var users = await new EfUserRepository(Context(), TimeProvider.System).FindByEmailAsync("anna@example.com");

        users.Should().NotBeNull();
        users!.Id.Should().Be(new SubjectId(id));
        users.Email.Should().Be("anna@example.com");
        users.DisplayName.Should().Be("Anna");
        users.Status.Should().Be(AccountStatus.Active);
        users.Roles.Should().Equal("user");
        users.PasswordHash.Should().StartWith("$2b$12$");
    }

    /// <summary>
    /// <c>users.email</c> is citext, so the comparison is the database's and is
    /// case-insensitive. A client-side comparison would quietly turn one account
    /// into two.
    /// </summary>
    [Fact]
    public async Task The_address_is_found_regardless_of_casing()
    {
        await WriteUser("Berta@Example.COM");

        var users = await new EfUserRepository(Context(), TimeProvider.System).FindByEmailAsync("berta@example.com");

        users.Should().NotBeNull();
    }

    [Fact]
    public async Task Every_account_status_is_read_as_it_stands()
    {
        await WriteUser("clara@example.com", status: "pending");
        await WriteUser("dora@example.com", status: "suspended");

        var repository = new EfUserRepository(Context(), TimeProvider.System);

        (await repository.FindByEmailAsync("clara@example.com"))!.Status
            .Should().Be(AccountStatus.Pending);
        (await repository.FindByEmailAsync("dora@example.com"))!.Status
            .Should().Be(AccountStatus.Suspended);
    }

    [Fact]
    public async Task An_unknown_address_gives_null()
    {
        var users = await new EfUserRepository(Context(), TimeProvider.System)
            .FindByEmailAsync("niemand@example.com");

        users.Should().BeNull();
    }

    /// <summary>
    /// The aggregate is a different object than the row, as it is in Python.
    /// Nothing done to it can reach the database.
    /// </summary>
    [Fact]
    public async Task The_aggregate_does_not_hang_on_the_change_tracker()
    {
        await WriteUser("emil@example.com");
        var context = Context();

        await new EfUserRepository(context, TimeProvider.System).FindByEmailAsync("emil@example.com");

        context.ChangeTracker.Entries().Should().BeEmpty();
    }
}
