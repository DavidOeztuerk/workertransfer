using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Consent.Tests;

/// <summary>What arrives here when somebody deletes their account.</summary>
/// <remarks>
/// This service is the one recipient where something deliberately <em>stays</em>
/// (ADR-0027 §5): the ledger is the proof that the erasure happened. Deleting it
/// along with everything else would make the promise unprovable — "we deleted"
/// with nothing left to check it against.
/// </remarks>
[Collection(PostgresCollection.Name)]
public class ErasureArrivalTests(Postgres postgres) : IAsyncLifetime
{
    private const string Secret = "erasure-secret-for-the-test";

    private WebApplicationFactory<Program> _service = null!;

    public Task InitializeAsync()
    {
        _service = Service(Secret);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _service.Dispose();
        return Task.CompletedTask;
    }

    private WebApplicationFactory<Program> Service(string secret) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:consent", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", TokenShape.PublicKey);
            host.UseSetting("Jwt:KeyId", TokenShape.KeyId);
            host.UseSetting(SecretStore.Variable, "test-master-key-for-the-suite");
            host.UseSetting("JwtSettings:Issuer", TokenShape.Issuer);
            host.UseSetting("JwtSettings:Audience", TokenShape.Audience);
            host.UseSetting("Erasure:Secret", secret);
            host.UseSetting("environment", "Development");
        });

    private HttpClient As(Guid subject)
    {
        var browser = _service.CreateClient();
        browser.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TokenShape.For(subject));
        return browser;
    }

    private static HttpRequestMessage ErasureRequest(Guid subject, string? secret)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/erasure")
        {
            Content = JsonContent.Create(new { user_id = subject })
        };

        if (secret is not null)
        {
            request.Headers.Add("X-Erasure-Secret", secret);
        }

        return request;
    }

    private async Task<Guid> PersonWithReleases()
    {
        var anna = Guid.CreateVersion7();
        var browser = As(anna);

        await browser.PostAsJsonAsync("/consent/grant", new
        {
            subject_id = anna, capability = "profile.visibility:public"
        });
        await browser.PostAsJsonAsync("/consent/grant", new
        {
            subject_id = anna, capability = "portfolio.visibility:public"
        });
        await browser.PostAsJsonAsync("/consent/revoke", new
        {
            subject_id = anna,
            capability = "portfolio.visibility:public",
            reason = "my employer must not see this"
        });

        return anna;
    }

    private async Task<List<(string Action, string? Reason)>> Rows(Guid subject)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT action::text, reason FROM consent_events WHERE subject_id = @subject ORDER BY id";
        command.Parameters.AddWithValue("subject", subject);

        var rows = new List<(string, string?)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1)));
        }

        return rows;
    }

    /// <summary>
    /// A default that opens in case of doubt would be the worst possible one
    /// for a route that erases a person — and an unset variable is exactly the
    /// case of doubt.
    /// </summary>
    [Fact]
    public async Task An_empty_secret_closes_the_endpoint()
    {
        using var without = Service(string.Empty);

        var response = await without.CreateClient().SendAsync(
            ErasureRequest(Guid.CreateVersion7(), "anything"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong")]
    public async Task Without_the_right_secret_nothing_is_erased(string? presented)
    {
        var anna = await PersonWithReleases();

        var response = await _service.CreateClient().SendAsync(ErasureRequest(anna, presented));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Rows(anna)).Should().NotContain(row => row.Action == "DELETE");
    }

    /// <summary>
    /// The chain stays as proof, one closing fact per capability ever held is
    /// appended, and the free text is cleared. The argument is not "we may
    /// retain" but that nothing maps a subject id to a person afterwards.
    /// </summary>
    [Fact]
    public async Task The_chain_stays_the_free_text_goes()
    {
        var anna = await PersonWithReleases();

        var response = await _service.CreateClient().SendAsync(ErasureRequest(anna, Secret));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var rows = await Rows(anna);

        rows.Should().Contain(row => row.Action == "GRANT", "the chain stays");
        rows.Should().Contain(row => row.Action == "REVOKE");
        rows.Count(row => row.Action == "DELETE")
            .Should().Be(2, "one closing row per capability ever held");
        rows.Should().OnlyContain(
            row => row.Reason == null, "the free text is cleared everywhere");
    }

    [Fact]
    public async Task After_the_erasure_no_release_holds_any_more()
    {
        var anna = await PersonWithReleases();

        await _service.CreateClient().SendAsync(ErasureRequest(anna, Secret));

        var checkResult = await As(Guid.CreateVersion7()).PostAsJsonAsync(
            "/consent/check", new { subject_id = anna, capability = "profile.visibility:public" });

        var state = JsonDocument.Parse(await checkResult.Content.ReadAsStringAsync()).RootElement;

        state.GetProperty("granted").GetBoolean().Should().BeFalse();
        state.GetProperty("deleted").GetBoolean().Should().BeTrue(
            "erased is not the same as revoked");
    }

    /// <summary>
    /// The cascade delivers at least once, so a redelivery has to be harmless —
    /// otherwise a dispatcher that crashes after delivering and before ticking
    /// off would double every closing fact.
    /// </summary>
    [Fact]
    public async Task Erasing_twice_changes_nothing_more()
    {
        var anna = await PersonWithReleases();

        await _service.CreateClient().SendAsync(ErasureRequest(anna, Secret));
        var after = await Rows(anna);

        var second = await _service.CreateClient().SendAsync(ErasureRequest(anna, Secret));

        second.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Rows(anna)).Should().HaveCount(after.Count);
    }

    /// <summary>The erasure leaves exactly one row behind: THAT it ran.</summary>
    /// <remarks>
    /// Measured in the database, not in the answer: a receipt can say "done"
    /// over an empty trail. Erased twice, because the cascade delivers at least
    /// once — a second delivery must not record a second erasure (ADR-0012,
    /// ADR-0027).
    /// </remarks>
    [Fact]
    public async Task The_erasure_leaves_exactly_one_audit_row()
    {
        var anna = await PersonWithReleases();

        (await _service.CreateClient().SendAsync(ErasureRequest(anna, Secret)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await _service.CreateClient().SendAsync(ErasureRequest(anna, Secret)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT count(*) FROM audit_events WHERE target_id = @subject AND action = 'consent_delete'";
        command.Parameters.AddWithValue("subject", anna);

        ((long)(await command.ExecuteScalarAsync())!)
            .Should().Be(1, "the trail says once that the erasure ran, and nothing about what it covered");
    }

    /// <summary>
    /// Nothing personal was retained, so the receipt says zero — even though
    /// the chain stays.
    /// </summary>
    /// <remarks>
    /// The two are not the same thing, and confusing them would be the easiest
    /// way to make this endpoint lie. <c>retained</c> counts rows a
    /// <em>retention switch</em> kept from deletion (ADR-0027 §3) — hired
    /// applications, paid transfers. The consent chain is not that: it is
    /// anonymised in place, and afterwards nothing maps a subject id to a
    /// person. A positive count here would tell the origin that data about
    /// somebody survived, which is exactly what did not happen.
    /// </remarks>
    [Fact]
    public async Task The_receipt_reports_zero_because_nothing_personal_stayed()
    {
        var anna = await PersonWithReleases();

        var response = await _service.CreateClient().SendAsync(ErasureRequest(anna, Secret));
        var receipt = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync()).RootElement;

        receipt.GetProperty("retained").GetInt32().Should().Be(0);
    }
}
