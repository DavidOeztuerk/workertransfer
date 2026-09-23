using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorkerTransfer.Identity.Infrastructure.Persistence;
using WorkerTransfer.Identity.Infrastructure.Mail;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Identity.Tests;

/// <summary>
/// Founding, inviting, joining, removing — over HTTP against the real schema.
/// </summary>
/// <remarks>
/// Roles are <em>enforced</em> here, which the Python service never did: it
/// checked <c>admin</c> against <c>member</c> nowhere and let the interface hide
/// the entries. A hidden button is not a permission.
/// </remarks>
[Collection(PostgresCollection.Name)]
public class CompanyJourneyTests(Postgres postgres) : IAsyncLifetime
{
    private const string Password = "secret-and-long-enough";

    private WebApplicationFactory<Program> _service = null!;
    private RecordingMailSender _sender = null!;

    public async Task InitializeAsync()
    {
        _sender = new RecordingMailSender(postgres.ConnectionString);

        _service = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:identity", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", TokenShape.PublicKey);
            host.UseSetting("Jwt:PrivateKey", TokenShape.PrivateKey);
            host.UseSetting("Jwt:KeyId", TokenShape.KeyId);
            host.UseSetting(SecretStore.Variable, "test-master-key-for-the-suite");
            host.UseSetting("JwtSettings:Issuer", TokenShape.Issuer);
            host.UseSetting("JwtSettings:Audience", TokenShape.Audience);
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
                services.Replace(ServiceDescriptor.Singleton<IMailSender>(_sender)));
        });

        using var scope = _service.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>()
            .Database.MigrateAsync();
    }

    public Task DisposeAsync()
    {
        _service.Dispose();
        return Task.CompletedTask;
    }

    private HttpClient Browser() =>
        _service.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    private string LastLink(string subjectPart)
    {
        var text = _sender.Sent.Last(mail =>
            mail.Subject.Contains(subjectPart, StringComparison.Ordinal)).Text;

        return Regex.Match(text, @"token=(?<token>[A-Za-z0-9_-]+)",
            RegexOptions.None, TimeSpan.FromSeconds(5)).Groups["token"].Value;
    }

    /// <summary>Registers, confirms and signs in — the way in for every actor here.</summary>
    private async Task<HttpClient> Person(string email, string? company = null)
    {
        var browser = Browser();

        var created = await browser.PostAsJsonAsync("/auth/register", new
        {
            email,
            password = Password,
            display_name = email.Split('@')[0],
            company_name = company
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var confirmed = await browser.PostAsJsonAsync(
            "/auth/verify-email", new { token = LastLink("bestätige") });
        confirmed.StatusCode.Should().Be(HttpStatusCode.OK);

        var signedIn = await browser.PostAsJsonAsync(
            "/auth/login", new { email, password = Password });
        signedIn.StatusCode.Should().Be(HttpStatusCode.OK);

        return browser;
    }

    private static string Address(string name, string domain) =>
        $"{name}-{Guid.NewGuid():N}@{domain}";

    private static string NewDomain() => $"company-{Guid.NewGuid():N}.example";

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static async Task<string> FirstCompany(HttpClient browser)
    {
        var mine = await Json(await browser.GetAsync("/me/companies"));
        return mine[0].GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Whoever_founds_is_administrator_of_their_company()
    {
        var boss = await Person(Address("boss", NewDomain()), company: "Example GmbH");

        var mine = await Json(await boss.GetAsync("/me/companies"));

        mine.GetArrayLength().Should().Be(1);
        mine[0].GetProperty("role").GetString().Should().Be("admin");
        mine[0].GetProperty("name").GetString().Should().Be("Example GmbH");
    }

    /// <summary>
    /// The route stays open even though registering can carry the intention:
    /// whoever signs up privately and founds later has no other way in — an
    /// invitation presupposes colleagues who do not exist yet.
    /// </summary>
    [Fact]
    public async Task Whoever_registered_privately_can_found_later()
    {
        var later = await Person(Address("anna", NewDomain()));

        var founded = await later.PostAsJsonAsync(
            "/companies", new { name = "Late Founding GmbH" });

        founded.StatusCode.Should().Be(HttpStatusCode.Created);
        (await Json(founded)).GetProperty("domain").GetString().Should().NotBeEmpty();
    }

    [Fact]
    public async Task An_invitation_leads_over_the_address_into_the_company()
    {
        var domain = NewDomain();
        var boss = await Person(Address("boss", domain), company: "Example GmbH");
        var company = await FirstCompany(boss);

        var colleague = Address("colleague", "other.example");
        var invited = await boss.PostAsJsonAsync(
            $"/companies/{company}/invitations", new { email = colleague, role = "member" });
        invited.StatusCode.Should().Be(HttpStatusCode.Created);

        var browser = await Person(colleague);
        var acceptedAt = await browser.PostAsJsonAsync(
            "/invitations/accept", new { token = LastLink("eingeladen") });

        acceptedAt.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(acceptedAt)).GetProperty("role").GetString().Should().Be("member");

        var mine = await Json(await browser.GetAsync("/me/companies"));
        mine.GetArrayLength().Should().Be(1);
    }

    /// <summary>
    /// A foreign address is an entirely ordinary case: the domain proves who
    /// owns it, not whom the company may let in.
    /// </summary>
    [Fact]
    public async Task A_foreign_address_may_be_invited()
    {
        var boss = await Person(Address("boss", NewDomain()), company: "Example GmbH");
        var company = await FirstCompany(boss);

        var invited = await boss.PostAsJsonAsync(
            $"/companies/{company}/invitations",
            new { email = Address("advisor", "gmail.com"), role = "member" });

        invited.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    /// <summary>
    /// Tokens get forwarded, and whoever holds the link is not necessarily who
    /// was invited. The server compares the address; the client asserts nothing.
    /// </summary>
    [Fact]
    public async Task Whoever_was_not_invited_does_not_get_in_with_the_token()
    {
        var boss = await Person(Address("boss", NewDomain()), company: "Example GmbH");
        var company = await FirstCompany(boss);

        await boss.PostAsJsonAsync(
            $"/companies/{company}/invitations",
            new { email = Address("intended", "other.example"), role = "member" });

        var token = LastLink("eingeladen");
        var stranger = await Person(Address("stranger", "other.example"));

        var attempt = await stranger.PostAsJsonAsync("/invitations/accept", new { token });

        attempt.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Only_an_administrator_may_invite()
    {
        var domain = NewDomain();
        var boss = await Person(Address("boss", domain), company: "Example GmbH");
        var company = await FirstCompany(boss);

        var colleague = Address("colleague", "other.example");
        await boss.PostAsJsonAsync(
            $"/companies/{company}/invitations", new { email = colleague, role = "member" });

        var browser = await Person(colleague);
        await browser.PostAsJsonAsync(
            "/invitations/accept", new { token = LastLink("eingeladen") });

        var attempt = await browser.PostAsJsonAsync(
            $"/companies/{company}/invitations",
            new { email = Address("one-more", "other.example"), role = "member" });

        attempt.StatusCode.Should().Be(
            HttpStatusCode.Forbidden, "Python checks this nowhere — here it does");
    }

    /// <summary>
    /// Like a foreign resource: somebody who is not a member must not be able to
    /// tell whether the company exists.
    /// </summary>
    [Fact]
    public async Task Whoever_is_not_a_member_does_not_see_the_company_at_all()
    {
        var boss = await Person(Address("boss", NewDomain()), company: "Example GmbH");
        var company = await FirstCompany(boss);
        var invented = Guid.NewGuid();

        var stranger = await Person(Address("stranger", NewDomain()));

        var withReal = await stranger.GetAsync($"/companies/{company}/members");
        var withInvented = await stranger.GetAsync($"/companies/{invented}/members");

        withReal.StatusCode.Should().Be(HttpStatusCode.NotFound);
        withInvented.StatusCode.Should().Be(withReal.StatusCode);
    }

    /// <summary>
    /// A company without an administrator is not deleted, it is orphaned: the
    /// domain stays claimed, the data stays, and nobody can invite or remove any
    /// more. That dead end appears with one click and can afterwards only be
    /// undone by hand.
    /// </summary>
    [Fact]
    public async Task The_last_administrator_may_not_leave()
    {
        var boss = await Person(Address("boss", NewDomain()), company: "Example GmbH");
        var company = await FirstCompany(boss);
        var subject = (await Json(await boss.GetAsync("/me"))).GetProperty("user_id").GetString();

        var attempt = await boss.DeleteAsync($"/companies/{company}/members/{subject}");

        attempt.StatusCode.Should().Be(
            HttpStatusCode.Conflict, "409, not 403 — she may remove, just not this one");
    }

    [Fact]
    public async Task An_administrator_removes_a_member_and_it_disappears_from_the_list()
    {
        var boss = await Person(Address("boss", NewDomain()), company: "Example GmbH");
        var company = await FirstCompany(boss);

        var colleague = Address("colleague", "other.example");
        await boss.PostAsJsonAsync(
            $"/companies/{company}/invitations", new { email = colleague, role = "member" });

        var browser = await Person(colleague);
        await browser.PostAsJsonAsync(
            "/invitations/accept", new { token = LastLink("eingeladen") });

        var subject = (await Json(await browser.GetAsync("/me"))).GetProperty("user_id").GetString();

        var removed = await boss.DeleteAsync($"/companies/{company}/members/{subject}");
        removed.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var members = await Json(await boss.GetAsync($"/companies/{company}/members"));
        members.GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task A_withdrawn_invitation_no_longer_carries()
    {
        var boss = await Person(Address("boss", NewDomain()), company: "Example GmbH");
        var company = await FirstCompany(boss);

        var colleague = Address("colleague", "other.example");
        var invited = await boss.PostAsJsonAsync(
            $"/companies/{company}/invitations", new { email = colleague, role = "member" });
        var token = LastLink("eingeladen");
        var id = (await Json(invited)).GetProperty("id").GetString();

        var back = await boss.DeleteAsync($"/companies/{company}/invitations/{id}");
        back.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await Json(await boss.GetAsync($"/companies/{company}/invitations")))
            .GetArrayLength().Should().Be(0);

        var browser = await Person(colleague);
        var attempt = await browser.PostAsJsonAsync("/invitations/accept", new { token });

        attempt.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Taking an invitation back is the reverse side of inviting — and therefore
    /// demands the same permission.
    /// </summary>
    /// <remarks>
    /// Until PBI-2 no policy asked at this endpoint: the command itself did
    /// check the role, but only behind the input, and a stranger read off the
    /// difference 400/403 that their body was fine. Now the policy decides
    /// before anything looks at the body.
    /// </remarks>
    [Fact]
    public async Task Only_an_administrator_may_take_an_invitation_back()
    {
        var boss = await Person(Address("boss", NewDomain()), company: "Example GmbH");
        var company = await FirstCompany(boss);

        var colleague = Address("colleague", "other.example");
        await boss.PostAsJsonAsync(
            $"/companies/{company}/invitations", new { email = colleague, role = "member" });

        var browser = await Person(colleague);
        await browser.PostAsJsonAsync(
            "/invitations/accept", new { token = LastLink("eingeladen") });

        // A second, open invitation — the target of the attempt.
        var open = await boss.PostAsJsonAsync(
            $"/companies/{company}/invitations",
            new { email = Address("third", "other.example"), role = "member" });
        var id = (await Json(open)).GetProperty("id").GetString();

        var asMember = await browser.DeleteAsync($"/companies/{company}/invitations/{id}");

        asMember.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            "a `member` may not take a colleague's invitation away");

        // The counter-probe in the same suite: otherwise an endpoint that
        // ALWAYS answers 403 would be indistinguishable from a properly
        // protected one.
        var asAdmin = await boss.DeleteAsync($"/companies/{company}/invitations/{id}");

        asAdmin.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    /// <summary>
    /// An id alone would let an administrator of one company act on another
    /// company's invitation.
    /// </summary>
    [Fact]
    public async Task Another_company_s_invitation_cannot_be_taken_back()
    {
        var one = await Person(Address("boss", NewDomain()), company: "One GmbH");
        var oneCompany = await FirstCompany(one);
        var invited = await one.PostAsJsonAsync(
            $"/companies/{oneCompany}/invitations",
            new { email = Address("colleague", "other.example"), role = "member" });
        var id = (await Json(invited)).GetProperty("id").GetString();

        var other = await Person(Address("chief", NewDomain()), company: "Other GmbH");
        var otherCompany = await FirstCompany(other);

        var attempt = await other.DeleteAsync(
            $"/companies/{otherCompany}/invitations/{id}");

        attempt.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Joining changes the memberships, not the running session. An automatic
    /// switch would push somebody out of the company they are working in,
    /// unasked (ADR-0018).
    /// </summary>
    [Fact]
    public async Task Joining_does_not_switch_the_running_session()
    {
        var boss = await Person(Address("boss", NewDomain()), company: "Example GmbH");
        var company = await FirstCompany(boss);

        var colleague = Address("colleague", "other.example");
        await boss.PostAsJsonAsync(
            $"/companies/{company}/invitations", new { email = colleague, role = "member" });

        var browser = await Person(colleague);
        await browser.PostAsJsonAsync(
            "/invitations/accept", new { token = LastLink("eingeladen") });

        var me = await Json(await browser.GetAsync("/me"));

        me.TryGetProperty("tenant_id", out var tenant).Should().BeTrue();
        tenant.ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>
    /// Whoever is removed is out at the NEXT request — with the same token.
    /// </summary>
    /// <remarks>
    /// <strong>The test that grounds the whole construction.</strong> The
    /// permissions deliberately do not stand in the token:
    /// `MembershipPermission` reads the membership per request out of the table.
    /// Were they in the token, a removal would take effect only when it expires
    /// — and at exactly this operation immediately is the only thing that
    /// counts.
    /// <para>
    /// The removed person's browser is NOT signed in again here. That is exactly
    /// the point: the same token that was enough a moment ago is no longer
    /// enough.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_removed_administrator_may_do_nothing_more_at_once()
    {
        var domain = NewDomain();
        var boss = await Person(Address("boss", domain), company: "Example GmbH");
        var company = await FirstCompany(boss);

        // Bring a second person in as ADMIN.
        var second = Address("second", "other.example");
        await boss.PostAsJsonAsync(
            $"/companies/{company}/invitations", new { email = second, role = "admin" });

        var browser = await Person(second);
        await browser.PostAsJsonAsync(
            "/invitations/accept", new { token = LastLink("eingeladen") });

        // She may invite now — the evidence that the policy lets anything
        // through at all and does not simply refuse everything.
        var before = await browser.PostAsJsonAsync(
            $"/companies/{company}/invitations",
            new { email = Address("third", "other.example"), role = "member" });
        before.StatusCode.Should().Be(HttpStatusCode.Created);

        // The boss removes her.
        var subject = await Subject(browser);
        var removed = await boss.DeleteAsync($"/companies/{company}/members/{subject}");
        removed.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // THE SAME token, no fresh sign-in.
        var afterwards = await browser.PostAsJsonAsync(
            $"/companies/{company}/invitations",
            new { email = Address("fourth", "other.example"), role = "member" });

        afterwards.StatusCode.Should().Be(
            HttpStatusCode.Forbidden,
            "the role is read per request, not out of the token — otherwise "
            + "the removal would take effect only at expiry");
    }

    /// <summary>
    /// A refusal by the policy carries the same problem document as everything
    /// else.
    /// </summary>
    /// <remarks>
    /// An authorisation does not throw, it short-circuits the answer — without
    /// <c>RejectionShape</c> a bare 403 with an empty body would fall out here,
    /// and a human being's report would have no correlation id.
    /// </remarks>
    [Fact]
    public async Task The_refusal_carries_the_usual_problem_document()
    {
        var domain = NewDomain();
        var boss = await Person(Address("boss", domain), company: "Example GmbH");
        var company = await FirstCompany(boss);

        var colleague = Address("colleague", "other.example");
        await boss.PostAsJsonAsync(
            $"/companies/{company}/invitations", new { email = colleague, role = "member" });

        var browser = await Person(colleague);
        await browser.PostAsJsonAsync(
            "/invitations/accept", new { token = LastLink("eingeladen") });

        var attempt = await browser.PostAsJsonAsync(
            $"/companies/{company}/invitations",
            new { email = Address("one-more", "other.example"), role = "member" });

        attempt.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        attempt.Content.Headers.ContentType!.MediaType
            .Should().Be("application/problem+json");

        var body = JsonDocument.Parse(
            await attempt.Content.ReadAsStringAsync()).RootElement;

        body.GetProperty("status").GetInt32().Should().Be(403);
        body.GetProperty("detail").GetString().Should().Be("not permitted");
        body.GetProperty("correlationId").GetString().Should().NotBeNullOrWhiteSpace();

        // WHICH policy was missing is not in it — that would be a map of the
        // permissions for anyone who asks.
        body.ToString().Should().NotContain("company.invite");
    }

    /// <summary>One's own subject id, as /me names it.</summary>
    private static async Task<string> Subject(HttpClient browser)
    {
        var response = await browser.GetFromJsonAsync<JsonElement>("/me");

        return response.GetProperty("user_id").GetString()!;
    }

}
