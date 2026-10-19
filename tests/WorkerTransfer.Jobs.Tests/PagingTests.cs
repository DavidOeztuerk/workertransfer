using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkerTransfer.Jobs.Application.Ports;
using WorkerTransfer.ServiceDefaults.Roles;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Jobs.Tests;

/// <summary>The job list pages by page numbers.</summary>
/// <remarks>
/// This suite pins the contract because it changed: before, <c>GET /jobs</c>
/// handed out a cursor (<c>next</c>), now a page number together with a total.
/// The CI job <c>images</c> checks exactly this shape, and so does the route
/// map — a silent relapse to the old field would be visible in neither.
/// <para>
/// The most important case is the last one: <strong>a skill filter has to count
/// the FILTERED set.</strong> Because this filter runs in memory (the vocabulary
/// stands in the code, not in SQL — ADR-0023), it was tempting to cut first and
/// filter afterwards. Then "page 1 of 9" would stand over three hits, and the
/// pager would offer eight empty pages.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public class PagingTests(Postgres postgres) : IAsyncLifetime
{
    private WebApplicationFactory<Program> _service = null!;

    public Task InitializeAsync()
    {
        _service = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:jobs", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", TokenShape.PublicKey);
            host.UseSetting("Jwt:KeyId", TokenShape.KeyId);
            host.UseSetting(SecretStore.Variable, "test-master-key-for-the-suite");
            host.UseSetting("JwtSettings:Issuer", TokenShape.Issuer);
            host.UseSetting("JwtSettings:Audience", TokenShape.Audience);
            host.UseSetting("Erasure:Secret", "withdrawal-secret");
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Scoped<IDrafter>(_ => new ProbeDrafter()));
                // Publishing demands an `admin` since PBI-2. This suite measures
                // the paging and not the role — so the probe answers like an
                // owner.
                services.Replace(ServiceDescriptor.Scoped<ICompanyRoles>(_ => new RoleProbe()));
            });
        });

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _service.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient AsCompany(Guid company)
    {
        var browser = _service.CreateClient();
        browser.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TokenShape.Company(Guid.CreateVersion7(), company));
        return browser;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    /// <summary>Creates <paramref name="howMany"/> published adverts.</summary>
    /// <remarks>
    /// The <paramref name="keyword"/> stands in the description and can be given
    /// back as <c>q</c>. All suites of this collection share ONE database —
    /// whoever checks an absolute total has to fence in their own rows, otherwise
    /// they count those of another suite along and stay green only as long as
    /// nobody else writes the same skill.
    /// </remarks>
    private async Task Create(
        HttpClient browser, int howMany, string keyword = "", params string[] skills)
    {
        for (var i = 0; i < howMany; i++)
        {
            var created = await browser.PostAsJsonAsync("/jobs", new
            {
                title = $"Position {Guid.NewGuid():N}",
                description = $"We build distributed systems. {keyword}",
                location = "Berlin",
                remote_mode = "hybrid",
                employment_type = "full_time",
                skills = skills.Length > 0 ? skills : ["C#"]
            });

            var id = (await Json(created)).GetProperty("id").GetGuid();
            await browser.PostAsync($"/jobs/{id}/publish", null);
        }
    }

    /// <summary>The answer carries everything a pager needs.</summary>
    [Fact]
    public async Task The_answer_carries_the_whole_pager()
    {
        await Create(AsCompany(Guid.CreateVersion7()), 1);

        var page = await Json(await _service.CreateClient().GetAsync("/jobs?page=1"));

        page.TryGetProperty("items", out _).Should().BeTrue();
        page.GetProperty("page").GetInt32().Should().Be(1);
        page.GetProperty("page_size").GetInt32().Should().Be(12);
        page.TryGetProperty("total_items", out _).Should().BeTrue();
        page.TryGetProperty("total_pages", out _).Should().BeTrue();
        page.TryGetProperty("has_next", out _).Should().BeTrue();
        page.TryGetProperty("has_previous", out _).Should().BeTrue();

        // The cursor is gone and must not come back: two ways through the same
        // list are two truths about its order.
        page.TryGetProperty("next", out _).Should().BeFalse();
    }

    /// <summary>Twelve without a value — and not everything.</summary>
    /// <remarks>
    /// The occasion for the whole paging: the list used to hand out twenty and
    /// offered no way to the rest.
    /// </remarks>
    [Fact]
    public async Task Without_a_value_it_is_twelve()
    {
        await Create(AsCompany(Guid.CreateVersion7()), 14);

        var page = await Json(await _service.CreateClient().GetAsync("/jobs"));

        page.GetProperty("items").GetArrayLength().Should().Be(12);
        page.GetProperty("has_next").GetBoolean().Should().BeTrue();
        page.GetProperty("has_previous").GetBoolean().Should().BeFalse();
    }

    /// <summary>The second page is a different one and does not overlap.</summary>
    [Fact]
    public async Task The_second_page_does_not_repeat_the_first()
    {
        await Create(AsCompany(Guid.CreateVersion7()), 8);

        var browser = _service.CreateClient();
        var first = await Json(await browser.GetAsync("/jobs?page=1&page_size=5"));
        var second = await Json(await browser.GetAsync("/jobs?page=2&page_size=5"));

        static IEnumerable<Guid> Ids(JsonElement page) =>
            page.GetProperty("items").EnumerateArray().Select(e => e.GetProperty("id").GetGuid());

        second.GetProperty("page").GetInt32().Should().Be(2);
        second.GetProperty("has_previous").GetBoolean().Should().BeTrue();
        Ids(first).Should().NotIntersectWith(Ids(second));
    }

    /// <summary>Nonsense in the query becomes the default, not an error.</summary>
    /// <remarks>
    /// A list that answers <c>?page=abc</c> with 400 is strict in a place where
    /// nobody gains anything. And the ceiling is no matter of taste:
    /// <c>page_size=100000</c> is no request but a way of keeping the database
    /// busy.
    /// </remarks>
    [Theory]
    [InlineData("?page=abc&page_size=xyz", 1, 12)]
    [InlineData("?page=-3", 1, 12)]
    [InlineData("?page_size=100000", 1, 96)]
    [InlineData("?page_size=0", 1, 12)]
    public async Task Nonsense_becomes_the_default(
        string query, int expectedPage, int expectedPageLength)
    {
        var page = await Json(await _service.CreateClient().GetAsync($"/jobs{query}"));

        page.GetProperty("page").GetInt32().Should().Be(expectedPage);
        page.GetProperty("page_size").GetInt32().Should().Be(expectedPageLength);
    }

    /// <summary>A skill filter counts what it hits — not what exists.</summary>
    /// <remarks>
    /// The filter runs in memory, because "Postgres" and "PostgreSQL" are the
    /// same requirement and this rule stands in the code (ADR-0023). Whoever
    /// cuts first and filters afterwards gets a total over the unfiltered set —
    /// and the pager offers pages on which nothing stands.
    /// </remarks>
    [Fact]
    public async Task A_skill_filter_counts_the_filtered_set()
    {
        var company = AsCompany(Guid.CreateVersion7());
        var keyword = $"skill{Guid.NewGuid():N}";
        await Create(company, 3, keyword, "Rust");
        await Create(company, 9, keyword, "Cobol");

        var page = await Json(
            await _service.CreateClient().GetAsync(
                $"/jobs?q={keyword}&skill=Rust&page_size=2"));

        page.GetProperty("total_items").GetInt32().Should().Be(3);
        page.GetProperty("total_pages").GetInt32().Should().Be(2);
        page.GetProperty("items").GetArrayLength().Should().Be(2);
    }

    /// <summary>An empty list has one page, not zero.</summary>
    /// <remarks>
    /// "Page 1 of 0" is a statement nobody can read, and a pager with zero pages
    /// has no state it could draw.
    /// </remarks>
    [Fact]
    public async Task An_empty_list_has_one_page()
    {
        var page = await Json(
            await _service.CreateClient().GetAsync("/jobs?q=doesnotexistxyzq"));

        page.GetProperty("total_items").GetInt32().Should().Be(0);
        page.GetProperty("total_pages").GetInt32().Should().Be(1);
        page.GetProperty("has_next").GetBoolean().Should().BeFalse();
    }
}
