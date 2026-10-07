using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Noelia.Core.Identity;
using WorkerTransfer.Messages.Application.Ports;
using WorkerTransfer.Messages.Infrastructure.Persistence;
using WorkerTransfer.Outbox;
using WorkerTransfer.ServiceDefaults;

namespace WorkerTransfer.Messages.Tests;

/// <summary>One conversation per process, and drafts only a human being releases (ADR-0047).</summary>
/// <remarks>
/// The whole way over the wire and into a real Postgres: between a handler and
/// what a browser gets lie the settings, the wiring and the field names.
/// </remarks>
[Collection(PostgresCollection.Name)]
public class MessageJourneyTests(Postgres postgres) : IAsyncLifetime
{
    private const string ErasureSecret = "erasure-secret";

    private static readonly Guid Company = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherCompany = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid Recruiter = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Colleague = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid Anna = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Stranger = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private static readonly Guid Application = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Missing = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000ff");

    private WebApplicationFactory<Program> _service = null!;
    private readonly ProbeProcess _process = new();
    private readonly ProbeMembers _members = new();
    private readonly ProbeDrafter _drafter = new();

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        // The suites share ONE database; without emptying every claim about
        // "exactly one row" would depend on who ran before.
        await using (var setup = Context())
        {
            await setup.Database.ExecuteSqlRawAsync(
                "TRUNCATE TABLE messages, conversations, outbox, audit_events");
        }

        _process.Applications[Application] = new ProcessParties(
            new SubjectId(Anna), new TenantId(Company), "Anna Muster");
        _members.Members[Company] = [Recruiter, Colleague];

        _service = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("ConnectionStrings:messages", postgres.ConnectionString);
            host.UseSetting("Jwt:PublicKey", TokenShape.PublicKey);
            host.UseSetting("Jwt:KeyId", TokenShape.KeyId);
            host.UseSetting(SecretStore.Variable, "test-master-key-for-the-suite");
            host.UseSetting("JwtSettings:Issuer", TokenShape.Issuer);
            host.UseSetting("JwtSettings:Audience", TokenShape.Audience);
            host.UseSetting("Erasure:Secret", ErasureSecret);
            host.UseSetting("environment", "Development");
            host.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Scoped<IProcessLookup>(_ => _process));
                services.Replace(ServiceDescriptor.Scoped<ICompanyMemberLookup>(_ => _members));
                services.Replace(ServiceDescriptor.Scoped<IReplyDrafter>(_ => _drafter));
            });
        });
    }

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        _service.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>A closed set — none sends a draft past a human being.</summary>
    [Fact]
    public void The_service_has_exactly_these_routes() =>
        _service.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(endpoint =>
                $"{Verbs(endpoint)} /{endpoint.RoutePattern.RawText?.TrimStart('/').TrimEnd('/')}")
            .Where(row => row.Contains("messages", StringComparison.Ordinal)
                          || row.Contains("erasure", StringComparison.Ordinal))
            .Should().BeEquivalentTo(
                "GET /messages/me",
                "GET /messages/{kind}/{reference:guid}",
                "POST /messages/{kind}/{reference:guid}",
                "POST /messages/{kind}/{reference:guid}/draft",
                "POST /messages/drafts/{id:guid}/release",
                "DELETE /messages/drafts/{id:guid}",
                "POST /erasure");

    [Fact]
    public async Task The_person_writes_and_every_member_hears_of_it()
    {
        var sent = await AsPerson(Anna).PostAsJsonAsync(Conversation(), Text("Gibt es schon einen Stand?"));

        sent.StatusCode.Should().Be(HttpStatusCode.Created);
        var message = await Json(sent);
        message.GetProperty("state").GetString().Should().Be("sent");
        message.GetProperty("side").GetString().Should().Be("person");

        var seen = await Json(await AsCompany(Recruiter).GetAsync(Conversation()));
        seen.GetProperty("side").GetString().Should().Be("company");
        seen.GetProperty("messages").EnumerateArray().Select(entry => entry.GetProperty("text").GetString())
            .Should().Equal("Gibt es schon einen Stand?");

        (await Notes()).Should().BeEquivalentTo([(Recruiter, "message_received"), (Colleague, "message_received")]);
    }

    [Fact]
    public async Task The_company_writes_and_the_person_hears_of_it_but_not_the_writer()
    {
        (await AsCompany(Recruiter).PostAsJsonAsync(Conversation(), Text("Wann passt ein Gespräch?")))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        (await Notes()).Should().BeEquivalentTo([(Anna, "message_received")]);

        var seen = await Json(await AsPerson(Anna).GetAsync(Conversation()));
        var only = seen.GetProperty("messages").EnumerateArray().Single();
        only.GetProperty("side").GetString().Should().Be("company");
        only.GetProperty("own").GetBoolean().Should().BeFalse();
        only.TryGetProperty("origin", out _).Should().BeFalse("the other side's origin is not shown");
        only.TryGetProperty("author", out _).Should().BeFalse();
    }

    [Fact]
    public async Task A_stranger_and_a_missing_process_get_the_same_answer()
    {
        await AsPerson(Anna).PostAsJsonAsync(Conversation(), Text("Hallo"));

        var stranger = await AsPerson(Stranger).GetAsync(Conversation());
        var otherCompany = await With(TokenShape.Company(Recruiter, OtherCompany)).GetAsync(Conversation());
        var missing = await AsPerson(Anna).GetAsync(Conversation(Missing));
        var unknownKind = await AsPerson(Anna).GetAsync(new Uri($"/messages/transfer/{Application}", UriKind.Relative));

        foreach (var answer in new[] { stranger, otherCompany, missing, unknownKind })
        {
            answer.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        Strip(await Raw(stranger)).Should().Be(Strip(await Raw(missing)));

        (await AsPerson(Stranger).PostAsJsonAsync(Conversation(), Text("Ich auch")))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Without_a_token_nothing_is_answered()
    {
        (await _service.CreateClient().GetAsync(Conversation())).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
        (await _service.CreateClient().GetAsync(new Uri("/messages/me", UriKind.Relative))).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_silent_process_service_is_503_never_404()
    {
        _process.Silent = true;

        var answer = await AsPerson(Anna).GetAsync(Conversation());

        answer.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task The_process_is_asked_at_every_read()
    {
        await AsPerson(Anna).GetAsync(Conversation());
        await AsPerson(Anna).GetAsync(Conversation());

        _process.Questions.Should().Be(2, "a removed member must stop at the next request, not at a cache's expiry");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_empty_message_is_refused(string text)
    {
        (await AsPerson(Anna).PostAsJsonAsync(Conversation(), Text(text)))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        await using var context = Context();
        (await context.Messages.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_too_long_message_is_refused()
    {
        (await AsPerson(Anna).PostAsJsonAsync(Conversation(), Text(new string('x', 4001))))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    // -----------------------------------------------------------------
    // Drafts (VISION-FRAGEN D2)
    // -----------------------------------------------------------------

    [Fact]
    public async Task A_draft_is_seen_only_by_its_own_side_and_tells_nobody()
    {
        var drafted = await AsCompany(Recruiter).PostAsync(Draft(), null);

        drafted.StatusCode.Should().Be(HttpStatusCode.Created);
        var draft = await Json(drafted);
        draft.GetProperty("state").GetString().Should().Be("draft");
        draft.GetProperty("origin").GetString().Should().Be("agent");

        var person = await Json(await AsPerson(Anna).GetAsync(Conversation()));
        person.GetProperty("messages").GetArrayLength().Should().Be(0, "the person never learns of a draft");

        var colleague = await Json(await AsCompany(Colleague).GetAsync(Conversation()));
        colleague.GetProperty("messages").EnumerateArray().Single()
            .GetProperty("state").GetString().Should().Be("draft");

        (await Notes()).Should().BeEmpty("a draft tells nobody");
    }

    [Fact]
    public async Task Released_it_is_a_message_in_the_releasers_words()
    {
        var draft = await Json(await AsCompany(Recruiter).PostAsync(Draft(), null));
        var id = draft.GetProperty("id").GetGuid();

        var released = await AsCompany(Colleague).PostAsJsonAsync(
            new Uri($"/messages/drafts/{id}/release", UriKind.Relative),
            new Dictionary<string, object?> { ["text"] = "Danke, wir melden uns bis Freitag." });

        released.StatusCode.Should().Be(HttpStatusCode.OK);

        var person = await Json(await AsPerson(Anna).GetAsync(Conversation()));
        var only = person.GetProperty("messages").EnumerateArray().Single();
        only.GetProperty("text").GetString().Should().Be("Danke, wir melden uns bis Freitag.");
        only.GetProperty("state").GetString().Should().Be("sent");

        (await Notes()).Should().BeEquivalentTo([(Anna, "message_received")]);
    }

    [Fact]
    public async Task The_other_side_can_neither_release_nor_discard_a_draft()
    {
        var draft = await Json(await AsCompany(Recruiter).PostAsync(Draft(), null));
        var id = draft.GetProperty("id").GetGuid();

        (await AsPerson(Anna).PostAsJsonAsync(
                new Uri($"/messages/drafts/{id}/release", UriKind.Relative), new Dictionary<string, object?>()))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await AsPerson(Anna).DeleteAsync(new Uri($"/messages/drafts/{id}", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        await using var context = Context();
        (await context.Messages.SingleAsync()).IsDraft.Should().BeTrue();
    }

    [Fact]
    public async Task A_discarded_draft_leaves_nothing()
    {
        var draft = await Json(await AsPerson(Anna).PostAsync(Draft(), null));

        (await AsPerson(Anna).DeleteAsync(
                new Uri($"/messages/drafts/{draft.GetProperty("id").GetGuid()}", UriKind.Relative)))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var context = Context();
        (await context.Messages.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_sent_message_cannot_be_released_again()
    {
        var sent = await Json(await AsPerson(Anna).PostAsJsonAsync(Conversation(), Text("Hallo")));

        (await AsPerson(Anna).PostAsJsonAsync(
                new Uri($"/messages/drafts/{sent.GetProperty("id").GetGuid()}/release", UriKind.Relative),
                new Dictionary<string, object?>()))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_model_reads_sent_messages_only_never_a_draft()
    {
        await AsPerson(Anna).PostAsJsonAsync(Conversation(), Text("Ist die Stelle noch frei?"));

        _drafter.Answer = "Unveröffentlichter Entwurf der Firma";
        await AsCompany(Recruiter).PostAsync(Draft(), null);

        _drafter.Answer = "Antwort der Person";
        await AsPerson(Anna).PostAsync(Draft(), null);

        var personContext = _drafter.Read.Last();
        personContext.History.Select(entry => entry.Text).Should().Equal("Ist die Stelle noch frei?");
        personContext.Writing.Should().Be(Domain.Conversations.Side.Person);
        personContext.PersonName.Should().Be("Anna Muster", "it travels only to be masked");
    }

    [Fact]
    public async Task Without_a_provider_the_answer_says_so_and_nothing_is_stored()
    {
        _drafter.Unavailable = true;

        var answer = await AsPerson(Anna).PostAsync(Draft(), null);

        answer.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await Raw(answer)).Should().Contain("No draft provider is set up.");

        await using var context = Context();
        (await context.Messages.CountAsync()).Should().Be(0);
        (await context.Conversations.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task My_conversations_lists_the_process_not_a_count()
    {
        await AsPerson(Anna).PostAsJsonAsync(Conversation(), Text("Hallo"));

        var mine = await Json(await AsPerson(Anna).GetAsync(new Uri("/messages/me", UriKind.Relative)));
        var ours = await Json(await AsCompany(Recruiter).GetAsync(new Uri("/messages/me", UriKind.Relative)));

        foreach (var list in new[] { mine, ours })
        {
            var row = list.EnumerateArray().Single();
            row.GetProperty("kind").GetString().Should().Be("application");
            row.GetProperty("reference").GetGuid().Should().Be(Application);
            row.EnumerateObject().Select(property => property.Name)
                .Should().BeEquivalentTo("kind", "reference", "last_message_at");
        }
    }

    // -----------------------------------------------------------------
    // Erasure (ADR-0027)
    // -----------------------------------------------------------------

    [Fact]
    public async Task The_erasure_takes_the_whole_conversation_and_leaves_the_trail()
    {
        await AsPerson(Anna).PostAsJsonAsync(Conversation(), Text("Hallo"));
        await AsCompany(Recruiter).PostAsJsonAsync(Conversation(), Text("Hallo Anna"));

        var erased = await Erase(Anna);

        erased.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Json(erased)).GetProperty("retained").GetInt32().Should().Be(0);

        await using var context = Context();
        (await context.Conversations.CountAsync()).Should().Be(0);
        (await context.Messages.CountAsync()).Should().Be(0, "the company's messages are about the person too");
        (await context.Set<OutboxRow>().CountAsync(row => row.UserId == Anna)).Should().Be(0);
        (await context.AuditEntries.CountAsync(row => row.TargetId == Anna && row.Action == "erased"))
            .Should().Be(1);
    }

    [Fact]
    public async Task An_erased_member_leaves_their_message_without_an_author()
    {
        await AsCompany(Recruiter).PostAsJsonAsync(Conversation(), Text("Hallo Anna"));
        await AsCompany(Recruiter).PostAsync(Draft(), null);

        await Erase(Recruiter);

        await using var context = Context();
        var left = await context.Messages.SingleAsync();
        left.Text.Should().Be("Hallo Anna", "it belongs to Anna's conversation");
        left.AuthorId.Should().BeNull();
        left.IsDraft.Should().BeFalse("an unreleased draft of the erased member is nobody's message");
    }

    [Fact]
    public async Task The_erasure_door_is_shut_without_the_secret()
    {
        var answer = await _service.CreateClient().PostAsJsonAsync(
            new Uri("/erasure", UriKind.Relative), new Dictionary<string, object?> { ["user_id"] = Anna });

        answer.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // -----------------------------------------------------------------

    private Task<HttpResponseMessage> Erase(Guid subject)
    {
        var client = _service.CreateClient();
        client.DefaultRequestHeaders.Add("X-Erasure-Secret", ErasureSecret);

        return client.PostAsJsonAsync(
            new Uri("/erasure", UriKind.Relative), new Dictionary<string, object?> { ["user_id"] = subject });
    }

    private async Task<List<(Guid, string)>> Notes()
    {
        await using var context = Context();

        return [.. (await context.Set<OutboxRow>().ToListAsync()).Select(row => (row.UserId, row.Kind))];
    }

    private static Uri Conversation(Guid? reference = null) =>
        new($"/messages/application/{reference ?? Application}", UriKind.Relative);

    private static Uri Draft() => new($"/messages/application/{Application}/draft", UriKind.Relative);

    private static Dictionary<string, object?> Text(string text) => new() { ["text"] = text };

    private static string Verbs(RouteEndpoint endpoint) =>
        string.Join(",", endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["?"]);

    /// <summary>The body without the correlation id, which differs by design.</summary>
    private static string Strip(string body) =>
        System.Text.RegularExpressions.Regex.Replace(body, "\"correlationId\":\"[^\"]*\"", string.Empty);

    private HttpClient AsPerson(Guid subject) => With(TokenShape.Person(subject));

    private HttpClient AsCompany(Guid member) => With(TokenShape.Company(member, Company));

    private HttpClient With(string token)
    {
        var client = _service.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static Task<string> Raw(HttpResponseMessage response) => response.Content.ReadAsStringAsync();

    private MessagesDbContext Context()
    {
        var builder = new DbContextOptionsBuilder<MessagesDbContext>();
        MessagesDbContextFactory.AtDesignTime(builder, postgres.ConnectionString);
        return new MessagesDbContext(builder.Options);
    }
}
