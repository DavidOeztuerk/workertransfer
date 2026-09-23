using System.Reflection;
using System.Text.Json.Serialization;
using FluentAssertions;

namespace WorkerTransfer.Ganzes.Tests;

/// <summary>
/// Every compound field name on the wire stands there expressly.
/// </summary>
/// <remarks>
/// <para><strong>This platform's wire is snake_case.</strong> Nine services set
/// it with <c>[JsonPropertyName]</c> on their contract types, and
/// <c>apps/web</c> reads and writes accordingly. A body <em>without</em> those
/// annotations falls back on camelCase from <c>NoeliaModule.JsonOptions</c> —
/// and then the same field is called something different on each side.</para>
///
/// <para><strong>Why that stays invisible for years.</strong> With single-word
/// fields camelCase and snake_case are <em>the same word</em>: <c>token</c>,
/// <c>email</c>, <c>capability</c>, <c>reason</c>. Only a compound name comes
/// apart. That is why this suite checks exactly those and only those.</para>
///
/// <para><strong>What it cost</strong>, four places, three kinds of damage —
/// all measured before they were fixed:</para>
/// <list type="bullet">
/// <item><c>RegisterBody.DisplayName</c>: the column is <c>NOT NULL</c>, so
/// <c>500</c> (after H4 <c>422</c>). Through the interface <strong>nobody could
/// register</strong>.</item>
/// <item><c>ProfileBody.RemoteOk</c>: a <c>bool</c> falls silently to
/// <c>false</c>. Whoever ticked "remote possible" got a <c>200</c> — and the
/// tick was gone.</item>
/// <item><c>GrantBody/RevokeBody/CheckBody.SubjectId</c>: arrived as
/// <c>Guid.Empty</c>, the guard compared it with the caller and answered
/// <c>403</c>. Through the interface <strong>the consent ledger was
/// unusable</strong> — the service every other one leans on.</item>
/// </list>
///
/// <para><strong>And why no test found it:</strong> the suites sent camelCase
/// themselves. Whoever checks against the server instead of against the contract
/// confirms every dialect the server happens to speak — including one nobody
/// else speaks. 660 green tests, and the interface could not get in.</para>
///
/// <para>The check is made on the <em>types</em> of the loaded assemblies, not
/// on the source: a regular expression would miss a field that comes from a base
/// type.</para>
/// </remarks>
public class DrahtvertragTests
{
    /// <summary>One type from each service's API layer.</summary>
    public static TheoryData<string, Type> ApiSchichten => new()
    {
        { "identity", typeof(Identity.Api.AuthEndpoints) },
        { "consent", typeof(Consent.Api.ConsentEndpoints) },
        { "profile", typeof(Profile.Api.ProfileEndpoints) },
        { "resume", typeof(Resume.Api.ResumeEndpoints) },
        { "portfolio", typeof(Portfolio.Api.PortfolioEndpoints) },
        { "jobs", typeof(Jobs.Api.RueckzugsEndpoints) },
        { "applications", typeof(Applications.Api.ErasureEndpoints) },
        { "companies", typeof(Companies.Api.EmployerEndpoints) },
        { "transfer", typeof(Transfer.Api.CaseEndpoints) },
        { "github", typeof(GitHub.Api.ErasureEndpoints) },
        { "notification", typeof(Notification.Api.ErasureEndpoints) },
        { "scout", typeof(Scout.Api.ErasureEndpoints) },
        { "advisor", typeof(Advisor.Api.ErasureEndpoints) },

        // The shared contracts belong here, and urgently so: this is where the
        // defect hid last. `ConsentQuestionV1` travels between two services, so
        // it was camelCase on BOTH sides and therefore internally consistent —
        // until the receiver came onto the platform's wire and the sender did
        // not. Measured: consent-service threw `A SubjectId must not be empty`,
        // profile-service reported 503 "the consent ledger did not answer", and
        // the candidate list was empty.
        { "contracts.consent", typeof(Contracts.Consent.ConsentQuestionV1) },
        { "contracts.erasure", typeof(Contracts.Erasure.ErasureV1) },
        { "contracts.identity", typeof(Contracts.Identity.Tokenform) },

        // And the contracts layer of EVERY service that has one. It lies in an
        // assembly of ITS OWN — whoever scans only the API layer does not see
        // it. That is exactly how `NotifyV1` slipped past.
        { "applications.contracts", typeof(Applications.Contracts.ApplicationV1) },
        { "companies.contracts", typeof(Companies.Contracts.EmployerProfileV1) },
        { "github.contracts", typeof(GitHub.Contracts.RepositoryV1) },
        { "jobs.contracts", typeof(Jobs.Contracts.JobV1) },
        { "notification.contracts", typeof(Notification.Contracts.NotifyV1) },
        { "portfolio.contracts", typeof(Portfolio.Contracts.EntryV1) },
        { "resume.contracts", typeof(Resume.Contracts.StationV1) },
        { "transfer.contracts", typeof(Transfer.Contracts.MarketStatusV1) },

        // The scout's contracts layer AND profile-service's: the two share a
        // seam (the internal profile search), and a field called camelCase there
        // never arrives. `profile.contracts` was empty until then — it stands
        // here from its first type on.
        { "profile.contracts", typeof(Profile.Contracts.ProfileHitV1) },
        { "scout.contracts", typeof(Scout.Contracts.HitV1) },
        { "advisor.contracts", typeof(Advisor.Contracts.ConversationV1) }
    };

    /// <summary>Whether camelCase and snake_case come apart for this name.</summary>
    /// <remarks>
    /// Exactly when another capital letter comes after the first character.
    /// <c>Token</c> does not come apart, <c>DisplayName</c> does.
    /// </remarks>
    private static bool Zusammengesetzt(string name) =>
        name.Skip(1).Any(char.IsUpper);

    /// <summary>No body type leaves a compound name open.</summary>
    [Theory]
    [MemberData(nameof(ApiSchichten))]
    public void Compound_names_stand_there_expressly(string service, Type ausDerSchicht)
    {
        var offen = ausDerSchicht.Assembly
            .GetTypes()
            // ALL records, including nested and private ones: `NotifyBodyV1` in
            // identity is a `private sealed record` inside the endpoint class,
            // and that is exactly the one a filter on `IsPublic` missed — while
            // it binds perfectly normally on the wire.
            .Where(type => !type.IsAbstract)
            // Body types are records here; that can be told from the generated clone.
            .Where(type => type.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.Instance) is not null)
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(eigenschaft => Zusammengesetzt(eigenschaft.Name))
                .Select(eigenschaft => (
                    Name: $"{type.Name}.{eigenschaft.Name}",
                    Draht: eigenschaft.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name))
                // If the annotation is missing, or a capital letter stands in
                // it, it is not this platform's wire. The second half is not
                // cosmetics: `NotifyV1` DID carry the annotation — with
                // the value "userId". A guard that only checks presence would
                // have waved that through and cemented the second dialect.
                .Where(entry => entry.Draht is null || entry.Draht.Any(char.IsUpper))
                .Select(entry => entry.Draht is null
                    ? entry.Name
                    : $"{entry.Name} (steht als \"{entry.Draht}\" auf dem Draht)"))
            .ToArray();

        offen.Should().BeEmpty(
            "{0} traegt einen zusammengesetzten Feldnamen ohne [JsonPropertyName]. "
            + "Ohne ihn heisst das Feld auf dem Draht camelCase, die Oberflaeche "
            + "schickt snake_case, und der Wert kommt nie an", service);
    }

    /// <summary>The list covers all thirteen API layers and the shared contracts.</summary>
    [Fact]
    public void The_list_covers_every_service()
    {
        var services = ApiSchichten.Select(row => (string)row[0]!).ToArray();

        services.Should().HaveCount(27);
        services.Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// The detection itself is right — otherwise the suite would be green
    /// because it finds nothing.
    /// </summary>
    [Theory]
    [InlineData("Token", false)]
    [InlineData("Email", false)]
    [InlineData("Capability", false)]
    [InlineData("DisplayName", true)]
    [InlineData("SubjectId", true)]
    [InlineData("RemoteOk", true)]
    public void The_detection_separates_correctly(string name, bool erwartet) =>
        Zusammengesetzt(name).Should().Be(erwartet);
}
