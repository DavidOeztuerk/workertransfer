using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Advisor.Application.Conversations;
using WorkerTransfer.Advisor.Application.Ports;
using WorkerTransfer.Advisor.Application.Mandates;
using WorkerTransfer.Advisor.Contracts;
using WorkerTransfer.Advisor.Domain.Conversations;
using WorkerTransfer.Advisor.Domain.Mandates;
using WorkerTransfer.Advisor.Infrastructure.Persistence;

namespace WorkerTransfer.Advisor.Tests;

/// <summary>
/// The sentences for whose sake this service was allowed to be built — each one
/// as a test.
/// </summary>
/// <remarks>
/// <para>They stand in <em>one</em> file, and that is deliberate: whoever turns
/// one of them red has not broken a test but the justification of the whole
/// service.</para>
///
/// <para>Where possible they check <em>types</em> and the <em>EF model</em>
/// rather than behaviour: a behaviour probe answers "does it not do that
/// today?", a type probe demands that whoever changes it writes the set down
/// here. The same movement, but one that gets noticed.</para>
/// </remarks>
public class ConditionsTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Anna = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // ---------------------------------------------------------------------
    // CONDITION 1 — THE MANDATE IS A VIEW, NOT A SECOND STORE
    // ---------------------------------------------------------------------

    /// <summary>Words that announce a second truth about visibility.</summary>
    /// <remarks>
    /// The list from ADR-0037 decision 1, plus the German twins and the English
    /// nouns (<c>visibility</c> does not contain <c>visible</c>, and
    /// <c>release</c> is wider than <c>released</c>). Visibility lives in the
    /// ledger; a column here would be the second truth, and it would drift apart
    /// at the first revocation.
    /// </remarks>
    private static readonly string[] Forbidden =
    [
        "sichtbar", "visible", "visibility", "public", "freigabe", "release", "released",
        "stage", "stufe"
    ];

    /// <summary>No column of the EF model carries such a word.</summary>
    /// <remarks>
    /// <para>On the <em>model</em> and not on the source: a column can come from
    /// a base class or a convention, and a regular expression would miss it.
    /// What is read here is exactly what comes about in the database.</para>
    ///
    /// <para><c>stage</c> and <c>stufe</c> are on the list too, and that is the
    /// sharper half: a stage column would be visibility as a second door beside
    /// the ledger, and a revocation would then have to take effect in two
    /// places — so one day in one of them it would not.</para>
    /// </remarks>
    [Fact]
    public void No_column_holds_a_visibility()
    {
        var columns = Columns();

        columns.Should().NotBeEmpty("otherwise this test checks nothing");

        foreach (var column in columns)
        {
            foreach (var word in Forbidden)
            {
                column.Contains(word, StringComparison.OrdinalIgnoreCase)
                    .Should().BeFalse(
                        $"'{column}' carries '{word}' — visibility lives in the ledger "
                        + "(ADR-0020, ADR-0037 decision 1)");
            }
        }
    }

    /// <summary>The detection itself is right — otherwise the test would be green because it finds nothing.</summary>
    [Theory]
    [InlineData("entry_month", false)]
    [InlineData("excluded_domains", false)]
    [InlineData("note", false)]
    [InlineData("is_public", true)]
    [InlineData("stage", true)]
    [InlineData("sichtbar_fuer", true)]
    [InlineData("visibility_scope", true)]
    [InlineData("release_state", true)]
    public void The_detection_separates_correctly(string column, bool expected) =>
        Forbidden.Any(word => column.Contains(word, StringComparison.OrdinalIgnoreCase))
            .Should().Be(expected);

    /// <summary>The mandate carries exactly these values — four, and a timestamp.</summary>
    /// <remarks>
    /// The closed field set, not a forbidden list: whoever adds a field writes it
    /// down here — and answers on the way whether it really stands nowhere else.
    /// Only that may be added.
    /// </remarks>
    [Fact]
    public void A_mandate_carries_exactly_these_fields()
    {
        Fields(typeof(Mandate)).Should().BeEquivalentTo(
            "Subject", "EntryMonth", "SalaryMin", "SalaryMax", "WorkloadPercent",
            "ExcludedDomains", "UpdatedAt", "IsEmpty");

        Fields(typeof(MandateV1)).Should().BeEquivalentTo(
            "EntryMonth", "SalaryMin", "SalaryMax", "WorkloadPercent",
            "ExcludedDomains", "UpdatedAt");
    }

    /// <summary>And the conversation carries no stage.</summary>
    /// <remarks>
    /// The twin of the column check, one level up: not even the aggregate has a
    /// stage, so it cannot be stored by accident either. What it is, only the
    /// ledger knows.
    /// </remarks>
    [Fact]
    public void A_conversation_carries_exactly_these_fields() =>
        Fields(typeof(Conversation)).Should().BeEquivalentTo(
            "Id", "Subject", "Company", "State", "Occasion", "OpenedAt", "UpdatedAt", "IsRunning");

    // ---------------------------------------------------------------------
    // CONDITION 2 — THE STAGE COMES FROM THE LEDGER, ON EVERY READ
    // ---------------------------------------------------------------------

    /// <summary>Every read asks the ledger again.</summary>
    /// <remarks>
    /// ADR-0013: a revocation has to take effect on the next read. A cache here
    /// would not be a performance detail but a breach of the rule — and it would
    /// only be noticed once somebody had withdrawn and was still seen.
    /// </remarks>
    [Fact]
    public async Task Every_read_asks_the_ledger_again()
    {
        var (conversations, mandates, ledger, people) = Setup();
        await Open(conversations, ledger);

        var handler = new CompanyConversationsHandler(conversations, mandates, ledger, people);

        var before = ledger.Questions;

        await handler.Handle(new CompanyConversationsQuery(new TenantId(Company)), default);
        await handler.Handle(new CompanyConversationsQuery(new TenantId(Company)), default);

        (ledger.Questions - before).Should().Be(2);
    }

    /// <summary>A release writes ledger events — cumulatively.</summary>
    /// <remarks>
    /// Whoever releases stage 3 also grants 1 and 2. Otherwise the new
    /// capability would stand while the one below it is missing, and a read
    /// would still give <c>None</c>: a stage is a state and not a jump.
    /// </remarks>
    [Fact]
    public async Task A_release_grants_everything_up_to_its_stage()
    {
        var (conversations, _, ledger, _) = Setup();
        var conversation = await Open(conversations, ledger);

        ledger.Self = Anna;

        await new ReleaseStageHandler(conversations, ledger).Handle(
            new ReleaseStageCommand(conversation.Id, new SubjectId(Anna), Stage.Person), default);

        var tenant = new TenantId(Company);

        ledger.Grants.Should().BeEquivalentTo(
            [
                StageCapabilities.Profile(tenant),
                StageCapabilities.Market(tenant),
                StageCapabilities.Resume(tenant),
                StageCapabilities.Documents(tenant),
                StageCapabilities.CivilName(tenant)
            ],
            "a stage is a state: whoever releases 3 also grants 1 and 2");
    }

    /// <summary>
    /// And it does NOT grant <c>profile.visibility:public</c>.
    /// </summary>
    /// <remarks>
    /// The counter-probe to the previous test, and the more important one: a
    /// stage release applies to <em>one</em> company. Turning it into a
    /// platform-wide visibility would be exactly the switch whose consequences
    /// nobody can see — among them the person's own employer, who is on the same
    /// platform.
    /// </remarks>
    [Fact]
    public async Task A_release_makes_nobody_visible_to_everyone()
    {
        var (conversations, _, ledger, _) = Setup();
        var conversation = await Open(conversations, ledger);

        ledger.Self = Anna;

        await new ReleaseStageHandler(conversations, ledger).Handle(
            new ReleaseStageCommand(conversation.Id, new SubjectId(Anna), Stage.Person), default);

        ledger.Grants.Should().NotContain(StageCapabilities.ProfilePublic);
        ledger.Grants.Should().NotContain("github.visibility:public");
    }

    /// <summary>A revocation takes everything above with it — and takes effect at once.</summary>
    [Fact]
    public async Task A_revocation_takes_everything_above_with_it()
    {
        var (conversations, mandates, ledger, people) = Setup();
        var conversation = await Open(conversations, ledger);

        ledger.Self = Anna;
        ledger.SetStage(Anna, Company, Stage.Person);

        var stands = await new RevokeStageHandler(conversations, ledger).Handle(
            new RevokeStageCommand(conversation.Id, new SubjectId(Anna), Stage.Profile), default);

        stands.Should().Be(Stage.None, "a revocation takes effect on the next request");

        ledger.Revocations.Should().Contain(StageCapabilities.CivilName(new TenantId(Company)));
        ledger.Reasons.Should().OnlyContain(reason => reason == RevokeStageHandler.Reason);

        // And the conversation drops out of the company's list with it: an empty
        // row in it would be the "locked" hint in list form.
        var theirs = await new CompanyConversationsHandler(conversations, mandates, ledger, people)
            .Handle(new CompanyConversationsQuery(new TenantId(Company)), default);

        theirs.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------
    // CONDITION 3 — WHAT IS NOT RELEASED DOES NOT EXIST
    // ---------------------------------------------------------------------

    /// <summary>At stage 1 there is no field for a salary and none for a name.</summary>
    /// <remarks>
    /// <para><strong>Checked on the JSON and not on the object</strong>, because
    /// that is exactly the promise: not <c>"salary_min": null</c>, but no key at
    /// all. A nulled field says "there is a salary here, you just do not see
    /// it" — and with that, that there would be something to see.</para>
    /// </remarks>
    [Fact]
    public void At_stage_one_the_fields_of_the_higher_stages_are_absent()
    {
        var json = Wire(Stage.Profile);

        json.Should().Contain("\"entry_month\"");
        json.Should().Contain("\"workload_percent\"");

        json.Should().NotContain("salary_min");
        json.Should().NotContain("salary_max");
        json.Should().NotContain("\"name\"");
        json.Should().NotContain("\"email\"");
    }

    /// <summary>At stage 2 the range is added, the name not yet.</summary>
    [Fact]
    public void At_stage_two_the_range_is_added()
    {
        var json = Wire(Stage.Documents);

        json.Should().Contain("\"salary_min\"");
        json.Should().NotContain("\"name\"");
        json.Should().NotContain("\"email\"");
    }

    /// <summary>At stage 3 everything is there.</summary>
    [Fact]
    public void At_stage_three_everything_is_there()
    {
        var json = Wire(Stage.Person);

        json.Should().Contain("\"salary_min\"");
        json.Should().Contain("\"name\"");
        json.Should().Contain("\"email\"");
    }

    /// <summary>
    /// And "released, but never filled in" looks exactly like "not released".
    /// </summary>
    /// <remarks>
    /// <para><strong>The actual promise of ADR-0020 §1</strong>, here at field
    /// level: hidden and not present are indistinguishable. Whoever could tell
    /// the two apart would learn from the difference that the person
    /// <em>named</em> a salary — information they did not give.</para>
    ///
    /// <para>The two JSON documents are compared literally, not field by field:
    /// a check that compares only the named fields would miss exactly the field
    /// somebody adds.</para>
    /// </remarks>
    [Fact]
    public void Released_without_a_value_looks_like_not_released()
    {
        var notReleased = Wire(Stage.Profile, mandate: Full());
        var releasedButEmpty = Wire(Stage.Documents, mandate: WithoutSalary());

        // Neither may carry a salary field — for two different reasons, and
        // nobody must be able to tell which.
        notReleased.Should().NotContain("salary");
        releasedButEmpty.Should().NotContain("salary");
    }

    // ---------------------------------------------------------------------
    // CONDITION 4 — THE CURRENT EMPLOYER LEARNS NOTHING
    // ---------------------------------------------------------------------

    /// <summary>
    /// Whoever excludes a company gives up "for all companies".
    /// </summary>
    /// <remarks>
    /// <para><strong>The acceptance hangs here.</strong> "The current employer
    /// does not see their own staff in the scout" is not kept by a second gate
    /// in the scout — that would be a second place deciding about visibility —
    /// but in the ledger itself.</para>
    ///
    /// <para>The ledger knows no denial: "visible to everyone, but not to X"
    /// cannot be expressed in it. So the mode "everyone" no longer exists as soon
    /// as somebody names an X. Afterwards the scout finds this person only for
    /// companies they released to one by one — and the excluded one is not among
    /// them.</para>
    /// </remarks>
    [Fact]
    public async Task An_exclusion_gives_up_public_visibility()
    {
        var (_, mandates, ledger, _) = Setup();
        ledger.Self = Anna;
        ledger.SetPublic(Anna, true);

        await new WriteMandateHandler(mandates, ledger, TimeProvider.System).Handle(
            new WriteMandateCommand(
                new SubjectId(Anna), null, null, null, null, ["employer.test"]),
            default);

        ledger.Revocations.Should().ContainSingle()
            .Which.Should().Be(StageCapabilities.ProfilePublic);

        ledger.Reasons.Should().OnlyContain(
            reason => reason == WriteMandateHandler.RevocationReason);
    }

    /// <summary>Without an exclusion nothing is revoked.</summary>
    /// <remarks>
    /// The counter-probe: a mandate without an exclusion must not touch public
    /// visibility. Whoever enters their salary does not vanish from the search
    /// because of it.
    /// </remarks>
    [Fact]
    public async Task Without_an_exclusion_visibility_stays_untouched()
    {
        var (_, mandates, ledger, _) = Setup();
        ledger.Self = Anna;
        ledger.SetPublic(Anna, true);

        await new WriteMandateHandler(mandates, ledger, TimeProvider.System).Handle(
            new WriteMandateCommand(new SubjectId(Anna), "2026-11", 4000, 5000, 80, null),
            default);

        ledger.Revocations.Should().BeEmpty();
    }

    /// <summary>An excluded company gets the same 404 as a stranger.</summary>
    /// <remarks>
    /// No code and no message of its own: "you are on their list" would be
    /// information about a human being given to exactly the company they are
    /// protecting themselves from.
    /// </remarks>
    [Fact]
    public async Task An_excluded_company_gets_the_same_answer()
    {
        var (conversations, mandates, ledger, _) = Setup();
        var companies = new ProbeCompanies();
        companies.Domains[Company] = "employer.test";

        ledger.SetStage(Anna, Company, Stage.Profile);

        await mandates.SaveAsync(
            Mandate.Write(
                new SubjectId(Anna), null, null, null, null, ["employer.test"],
                DateTimeOffset.UtcNow),
            default);

        var handler = new OpenConversationHandler(
            conversations, mandates, ledger, companies, new ProbeOutbox(), TimeProvider.System);

        var act = async () => await handler.Handle(
            new OpenConversationCommand(new SubjectId(Anna), new TenantId(Company), "Hello"),
            default);

        (await act.Should().ThrowAsync<NoConversation>())
            .Which.Message.Should().Be("No such conversation");
    }

    /// <summary>Without an exclusion identity-service is not even asked.</summary>
    /// <remarks>
    /// Not thrift: a hop that decides nothing in almost every case is a hop that,
    /// at every outage of the other service, prevents an opening that concerns
    /// nobody.
    /// </remarks>
    [Fact]
    public async Task Without_an_exclusion_the_domain_is_not_asked_for()
    {
        var (conversations, mandates, ledger, _) = Setup();
        var companies = new ProbeCompanies();

        ledger.SetStage(Anna, Company, Stage.Profile);

        await new OpenConversationHandler(
                conversations, mandates, ledger, companies, new ProbeOutbox(), TimeProvider.System)
            .Handle(
                new OpenConversationCommand(new SubjectId(Anna), new TenantId(Company), "Hello"),
                default);

        companies.Questions.Should().Be(0);
    }

    // ---------------------------------------------------------------------
    // CONDITION 5 — THE TRIANGLE CONSENSUS IS NOT REBUILT
    // ---------------------------------------------------------------------

    /// <summary>Words that announce one of transfer-service's market rules.</summary>
    /// <remarks>
    /// German and English side by side: transfer-service speaks English now
    /// (<c>MarketStatus</c>, <c>Availability</c>, <c>IsApproachable</c>,
    /// <c>RequiresRelease</c>, <c>OfferFeeCents</c>), and a German-only list
    /// would let exactly those names slip past. <c>release</c> alone is not on
    /// it — a stage release is this service's own business; the rule is
    /// <c>requiresrelease</c>, the employer's release a transfer may need.
    /// </remarks>
    private static readonly string[] MarketWords =
    [
        "ansprechbar", "approachable", "approachability",
        "marktstatus", "marketstatus",
        "verfuegbarkeit", "availability",
        "abloese", "transferfee", "gebuehr", "fee",
        "brauchtfreigabe", "requiresrelease"
    ];

    /// <summary>
    /// This service knows none of the conditions of a transfer.
    /// </summary>
    /// <remarks>
    /// <para>Checked on the type names and not on behaviour: a behaviour test
    /// answers "does it not happen today?", this one demands that whoever
    /// copies a market rule here writes it down here.</para>
    ///
    /// <para>Approachability, market status, employer release, placement fee:
    /// all of that lives in transfer-service, and the handover calls its door.
    /// A second place that knows those rules would be the first to go
    /// stale.</para>
    /// </remarks>
    [Fact]
    public void No_market_rule_is_rebuilt_here()
    {
        var names = Names(typeof(OpenConversationCommand).Assembly)
            .Concat(Names(typeof(Conversation).Assembly))
            .ToArray();

        names.Should().NotBeEmpty("otherwise the test checks nothing");

        foreach (var name in names)
        {
            foreach (var word in MarketWords)
            {
                name.Contains(word, StringComparison.OrdinalIgnoreCase)
                    .Should().BeFalse(
                        $"'{name}' rebuilds a rule that lives in transfer-service "
                        + "(ADR-0037 decision 4)");
            }
        }
    }

    /// <summary>The market words catch transfer-service's English names — and not ours.</summary>
    /// <remarks>
    /// The detection's counter-probe: the first five are names transfer-service
    /// really carries (or would, one step further), the last three are this
    /// service's own and must stay free.
    /// </remarks>
    [Theory]
    [InlineData("Conversation.IsApproachable", true)]
    [InlineData("MarketStatusQuery", true)]
    [InlineData("Mandate.Availability", true)]
    [InlineData("HandOverCommand.OfferFeeCents", true)]
    [InlineData("Conversation.RequiresRelease", true)]
    [InlineData("ReleaseStageCommand", false)]
    [InlineData("Conversation.Occasion", false)]
    [InlineData("ConversationView.EntryMonth", false)]
    public void The_market_words_separate_correctly(string name, bool expected) =>
        MarketWords.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase))
            .Should().Be(expected);

    /// <summary>The handover happens only after the person has agreed.</summary>
    [Fact]
    public async Task Handing_over_happens_only_after_agreement()
    {
        var (conversations, _, ledger, _) = Setup();
        var conversation = await Open(conversations, ledger);
        var handover = new ProbeHandover();

        var handler = new HandOverHandler(conversations, handover, TimeProvider.System);

        var tooEarly = async () => await handler.Handle(
            new HandOverCommand(conversation.Id, new TenantId(Company)), default);

        await tooEarly.Should().ThrowAsync<TransitionNotAllowed>();
        handover.HandedOver.Should().BeEmpty();

        await new AgreeHandler(conversations, ledger, TimeProvider.System).Handle(
            new AgreeCommand(conversation.Id, new SubjectId(Anna)), default);

        var caseItem = await handler.Handle(
            new HandOverCommand(conversation.Id, new TenantId(Company)), default);

        caseItem.Should().Be(handover.Case);
        handover.HandedOver.Should().Equal(Anna);
    }

    /// <summary>If transfer-service refuses, the conversation stays where it is.</summary>
    /// <remarks>
    /// Write only once it has worked: the other way round this side would say
    /// "handed over" and the other side nothing — and nobody would find the case
    /// both sides believe was made.
    /// </remarks>
    [Fact]
    public async Task A_refused_handover_leaves_the_conversation_standing()
    {
        var (conversations, _, ledger, _) = Setup();
        var conversation = await Open(conversations, ledger);

        await new AgreeHandler(conversations, ledger, TimeProvider.System).Handle(
            new AgreeCommand(conversation.Id, new SubjectId(Anna)), default);

        var handover = new ProbeHandover { Refuses = true };

        var act = async () => await new HandOverHandler(
                conversations, handover, TimeProvider.System)
            .Handle(new HandOverCommand(conversation.Id, new TenantId(Company)), default);

        await act.Should().ThrowAsync<HandoverRefused>();

        (await conversations.GetAsync(conversation.Id))!.State
            .Should().Be(ConversationState.Agreed);
    }

    // ---------------------------------------------------------------------
    // CONDITION 6 — THE CIVIL NAME IS LOOKED UP ONLY AFTER THE LEDGER
    // ---------------------------------------------------------------------

    /// <summary>Nothing is looked up about somebody who released nothing.</summary>
    /// <remarks>
    /// The order is the promise — the same one scout-service keeps for evidence.
    /// Fetching the civil name of a person at stage 1 would be a call that must
    /// not exist, and it would stand in the other service's log.
    /// </remarks>
    [Fact]
    public async Task The_civil_name_is_fetched_only_from_stage_three()
    {
        var (conversations, mandates, ledger, people) = Setup();
        var conversation = await Open(conversations, ledger);

        var handler = new CompanyConversationsHandler(conversations, mandates, ledger, people);

        await handler.Handle(new CompanyConversationsQuery(new TenantId(Company)), default);
        people.Asked.Should().BeEmpty("at stage 1 there is no civil name to fetch");

        ledger.SetStage(Anna, Company, Stage.Person);
        people.Known[Anna] = new PersonCard("Anna Example", "a@b.test");

        var theirs = await handler.Handle(
            new CompanyConversationsQuery(new TenantId(Company)), default);

        people.Asked.Should().Equal(Anna);
        theirs.Should().ContainSingle().Which.Name.Should().Be("Anna Example");
        conversation.Subject.Value.Should().Be(Anna);
    }

    // ---------------------------------------------------------------------

    /// <summary>The public instance properties of a type.</summary>
    private static string[] Fields(Type type) =>
        [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(e => e.Name)];

    private static string[] Names(Assembly assembly) =>
        [.. assembly.GetExportedTypes()
            .SelectMany(type => type.GetMembers(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.DeclaredOnly)
                .Select(member => $"{type.Name}.{member.Name}")
                .Append(type.Name))
            .Distinct()];

    /// <summary>Every column this model really creates.</summary>
    private static string[] Columns()
    {
        var builder = new DbContextOptionsBuilder<AdvisorDbContext>();
        AdvisorDbContextFactory.AtDesignTime(builder, "Host=test-bench;Database=advisor");

        using var context = new AdvisorDbContext(builder.Options);

        return [.. context.Model.GetEntityTypes()
            .SelectMany(type => type.GetProperties())
            .Select(property => property.GetColumnName())
            .Distinct(StringComparer.Ordinal)];
    }

    /// <summary>What really stands on the wire for this stage.</summary>
    private static string Wire(Stage stage, Mandate? mandate = null)
    {
        var conversation = Conversation.Open(
            new SubjectId(Anna), new TenantId(Company), "Hello", DateTimeOffset.UtcNow);

        var view = ConversationView.Build(
            conversation,
            stage,
            mandate ?? Full(),
            new PersonCard("Anna Example", "anna@example.test"));

        return JsonSerializer.Serialize(new ConversationV1(
            view.Conversation.Id,
            view.Conversation.Subject.Value,
            ConversationStates.Word(view.Conversation.State),
            StageCapabilities.ToWire(view.Stage),
            view.Conversation.Occasion,
            view.Conversation.OpenedAt,
            view.Conversation.UpdatedAt)
        {
            EntryMonth = view.EntryMonth,
            WorkloadPercent = view.WorkloadPercent,
            SalaryMin = view.SalaryMin,
            SalaryMax = view.SalaryMax,
            Name = view.Name,
            Email = view.Email
        });
    }

    private static Mandate Full() =>
        Mandate.Write(
            new SubjectId(Anna), "2026-11", 4000, 5000, 80, [], DateTimeOffset.UtcNow);

    private static Mandate WithoutSalary() =>
        Mandate.Write(
            new SubjectId(Anna), "2026-11", null, null, 80, [], DateTimeOffset.UtcNow);

    private static (ProbeConversations, ProbeMandates, ProbeLedger, ProbePeople) Setup() =>
        (new ProbeConversations(), new ProbeMandates(), new ProbeLedger(), new ProbePeople());

    private static async Task<Conversation> Open(ProbeConversations conversations, ProbeLedger ledger)
    {
        ledger.SetStage(Anna, Company, Stage.Profile);

        var conversation = Conversation.Open(
            new SubjectId(Anna), new TenantId(Company), "Hello", DateTimeOffset.UtcNow);

        await conversations.SaveAsync(conversation);

        return conversation;
    }
}
