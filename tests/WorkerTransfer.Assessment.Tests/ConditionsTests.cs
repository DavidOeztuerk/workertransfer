using System.Reflection;
using FluentAssertions;
using Noelia.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WorkerTransfer.Assessment.Application.Ports;
using WorkerTransfer.Assessment.Application.Cases;
using WorkerTransfer.Assessment.Contracts;
using WorkerTransfer.Assessment.Domain.Cases;
using WorkerTransfer.Assessment.Infrastructure.Persistence;

namespace WorkerTransfer.Assessment.Tests;

/// <summary>
/// The three sentences for whose sake this service was allowed to be built — each
/// as a test.
/// </summary>
/// <remarks>
/// <para>They stand in <em>one</em> file, and that is on purpose: whoever turns
/// one of them red has not broken a test but the justification of the whole
/// service (ADR-0042).</para>
///
/// <para>Where possible the check is made on <em>types</em> and on the <em>EF
/// model</em> instead of on behaviour: a behaviour probe answers "does it not do
/// that today?", a type probe demands that whoever changes it writes the set down
/// here. The same movement, but one that stands out.</para>
/// </remarks>
public class ConditionsTests
{
    private static readonly Guid Company = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherCompany = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid Anna = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Now =
        new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);

    // ---------------------------------------------------------------------
    // CONDITION 1 — THE EVALUATION BELONGS TO THE CASE, NOT TO THE HUMAN BEING
    // ---------------------------------------------------------------------

    /// <summary>Words from which a grade about a human being would arise.</summary>
    /// <remarks>
    /// <para>The twin of <c>Adr0022Tests</c> and of scout-service's conditions.
    /// Forbidden is the <em>rating number</em>, not the number: <c>hours</c> is
    /// expressly not on this list, because it is about the <em>task</em> and not
    /// about the human being. That is exactly ADR-0022's dividing line —
    /// requirement in, evidence out; never human in, number out.</para>
    ///
    /// <para><c>stufe</c> and <c>level</c> are on it too: a stage would be a
    /// ranking by another name. The German words stay on the list because they
    /// catch a German name, and a German name is exactly what could slip back
    /// in.</para>
    /// </remarks>
    private static readonly string[] Forbidden =
    [
        "score", "rank", "grade", "rating", "punkte", "note", "sterne", "stars",
        "percent", "prozent", "weight", "gewicht", "fit", "passung", "level", "stufe"
    ];

    /// <summary>No column of the EF model carries such a word.</summary>
    /// <remarks>
    /// On the <em>model</em> and not on the source: a column can come from a base
    /// class or a convention, and a regular expression would overlook it. What is
    /// read here is exactly what comes into being in the database.
    /// </remarks>
    [Fact]
    public void No_column_carries_a_grade()
    {
        var columns = Columns();

        columns.Should().NotBeEmpty("otherwise this test checks nothing");
        columns.Should().Contain("hours", "the allowed number must really be there");

        foreach (var column in columns)
        {
            foreach (var word in Forbidden)
            {
                column.Contains(word, StringComparison.OrdinalIgnoreCase)
                    .Should().BeFalse(
                        $"'{column}' carries '{word}' — an evaluation has text and no number "
                        + "(ADR-0022, ADR-0042 §1)");
            }
        }
    }

    /// <summary>And no name in domain and contract.</summary>
    /// <remarks>
    /// Two assemblies, because they are the two places where a number could arise:
    /// the aggregate stores it, the contract would give it out. The application
    /// layer computes nothing.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Assemblies))]
    public void No_name_carries_a_grade(string which, Assembly assembly)
    {
        var names = Names(assembly);

        names.Should().NotBeEmpty($"{which} must be readable, otherwise this checks nothing");

        foreach (var name in names)
        {
            foreach (var word in Forbidden)
            {
                name.Contains(word, StringComparison.OrdinalIgnoreCase)
                    .Should().BeFalse($"'{name}' in {which} carries '{word}'");
            }
        }
    }

    /// <summary>The two assemblies that must compute nothing.</summary>
    public static TheoryData<string, Assembly> Assemblies => new()
    {
        { "Domain", typeof(Case).Assembly },
        { "Contracts", typeof(CaseV1).Assembly }
    };

    /// <summary>The detection itself is right — otherwise the test would be green because it finds nothing.</summary>
    [Theory]
    [InlineData("hours", false)]
    [InlineData("evaluation_text", false)]
    [InlineData("submission_url", false)]
    [InlineData("due_at", false)]
    [InlineData("score", true)]
    [InlineData("gesamtnote", true)]
    [InlineData("fit_percent", true)]
    [InlineData("sterne", true)]
    public void The_detection_separates_correctly(string name, bool expected) =>
        Forbidden.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase))
            .Should().Be(expected);

    /// <summary>An evaluation carries exactly these fields — text, outcome, time.</summary>
    /// <remarks>
    /// The closed set of fields, not a forbidden list: whoever adds a field writes
    /// it down here. <strong>And it is exactly ONE text field</strong> — no
    /// internal one beside it, no remark, no "just for us". Where there were two,
    /// the truth would stand in the second, and the person would read the other
    /// (ADR-0042 §2).
    /// </remarks>
    [Fact]
    public void An_evaluation_carries_exactly_these_fields()
    {
        Fields(typeof(Evaluation)).Should().BeEquivalentTo("Text", "Outcome", "At");

        Fields(typeof(EvaluationV1)).Should().BeEquivalentTo("Outcome", "Text", "EvaluatedAt");
    }

    /// <summary>And an outcome knows exactly two values.</summary>
    /// <remarks>
    /// Two, not three and not five: a scale with three steps is a grade in words.
    /// "Go on" or "do not go on" is a statement about the case; anything finer
    /// would be one about the human being.
    /// </remarks>
    [Fact]
    public void An_outcome_knows_exactly_two_values() =>
        Enum.GetNames<Outcome>().Should().BeEquivalentTo("Accepted", "Rejected");

    /// <summary>The store knows two questions — and no third.</summary>
    /// <remarks>
    /// <para>"This company's cases" and "my cases". A method "the cases of
    /// <em>this person</em>", callable by a company, would be the agency from
    /// ADR-0042 §1 — and it does not exist here, so that nobody finds it.</para>
    ///
    /// <para>On the port and not on the implementation: whoever writes a second
    /// implementation cannot get past this set.</para>
    /// </remarks>
    [Fact]
    public void The_store_knows_exactly_these_questions() =>
        typeof(ICaseStore).GetMethods().Select(member => member.Name)
            .Should().BeEquivalentTo(
                "GetAsync", "ForPersonAsync", "ForCompanyAsync", "SaveAsync", "DeleteAsync");

    /// <summary>Another company sees nothing — the same 404 as "does not exist".</summary>
    [Fact]
    public async Task Another_company_does_not_see_the_case()
    {
        var (cases, ledger, _, clock) = Setup();
        var caseItem = await SetTask(cases, ledger, clock);

        // Even with a release to the other company: the case does not belong to
        // it, and an evaluation never travels to a second company.
        ledger.Set(Anna, OtherCompany, true);

        var handler = new CaseHandler(cases, ledger);

        await FluentActions
            .Awaiting(() => handler.Handle(
                new CaseQuery(caseItem.Id, new SubjectId(OtherCompany), new TenantId(OtherCompany)),
                default))
            .Should().ThrowAsync<NoCase>();
    }

    // ---------------------------------------------------------------------
    // CONDITION 2 — THE PERSON SEES THE EVALUATION, ALWAYS
    // ---------------------------------------------------------------------

    /// <summary>A rejection without text is refused.</summary>
    /// <remarks>
    /// <strong>The core of the second condition.</strong> Rejecting and giving a
    /// reason are one step and not two — the second could otherwise be left out,
    /// and that is exactly the trade this service must not broker: work for
    /// silence.
    /// </remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_rejection_without_text_is_refused(string? text) =>
        FluentActions.Invoking(() => Evaluation.Write(text, Outcome.Rejected, Now))
            .Should().Throw<InputError>();

    /// <summary>And an acceptance without text too — the rule knows no exception.</summary>
    [Fact]
    public void An_acceptance_without_text_is_refused_too() =>
        FluentActions.Invoking(() => Evaluation.Write(" ", Outcome.Accepted, Now))
            .Should().Throw<InputError>();

    /// <summary>The counter-probe: with text it works.</summary>
    [Fact]
    public void With_text_it_is_evaluated() =>
        Evaluation.Write("Cleanly solved, only the error handling is missing.",
            Outcome.Rejected, Now).Text.Should().StartWith("Cleanly");

    /// <summary>The person side never asks the ledger.</summary>
    /// <remarks>
    /// Neither for the list nor for the single case. Checking one's own consent to
    /// see one's own work would not just be a superfluous hop — whoever has revoked
    /// would otherwise no longer get at their evaluations.
    /// </remarks>
    [Fact]
    public async Task The_person_side_never_asks_the_ledger()
    {
        var (cases, ledger, _, clock) = Setup();
        var caseItem = await SetTask(cases, ledger, clock);
        var before = ledger.Questions;

        await new MyCasesHandler(cases).Handle(
            new MyCasesQuery(new SubjectId(Anna)), default);

        await new CaseHandler(cases, ledger).Handle(
            new CaseQuery(caseItem.Id, new SubjectId(Anna), null), default);

        ledger.Questions.Should().Be(before, "one's own work needs no release");
    }

    /// <summary>After a revocation the person reads on — the company does not.</summary>
    /// <remarks>
    /// <strong>This service's sharpest sentence.</strong> An evaluation one can
    /// lock oneself out of by taking visibility back would be a judgement the
    /// judged never reads — exactly what is not built here. And the other half
    /// holds just the same: a revocation takes effect at the next read
    /// (ADR-0013).
    /// </remarks>
    [Fact]
    public async Task After_a_revocation_the_person_reads_on()
    {
        var (cases, ledger, outbox, clock) = Setup();
        var caseItem = await SetTask(cases, ledger, clock);

        caseItem.Submit(Submission.Write("Done.", null, clock.GetUtcNow()), clock.GetUtcNow());

        await new EvaluateHandler(cases, ledger, outbox, clock).Handle(
            new EvaluateCommand(caseItem.Id, new TenantId(Company), Outcome.Rejected, "Too thin."),
            default);

        // And now she takes her visibility back.
        ledger.Set(Anna, Company, false);

        var handler = new CaseHandler(cases, ledger);

        var hers = await handler.Handle(
            new CaseQuery(caseItem.Id, new SubjectId(Anna), null), default);

        hers.Evaluation!.Text.Should().Be("Too thin.");

        await FluentActions
            .Awaiting(() => handler.Handle(
                new CaseQuery(caseItem.Id, new SubjectId(Guid.NewGuid()), new TenantId(Company)),
                default))
            .Should().ThrowAsync<NoCase>();
    }

    /// <summary>The company list asks the ledger again at every read.</summary>
    [Fact]
    public async Task The_company_list_asks_the_ledger_at_every_read()
    {
        var (cases, ledger, _, clock) = Setup();
        await SetTask(cases, ledger, clock);

        var handler = new CompanyCasesHandler(cases, ledger);
        var before = ledger.Questions;

        (await handler.Handle(new CompanyCasesQuery(new TenantId(Company)), default))
            .Should().ContainSingle();

        ledger.Questions.Should().Be(before + 1);

        // Revocation — and the very next list is empty, not the one after.
        ledger.Set(Anna, Company, false);

        (await handler.Handle(new CompanyCasesQuery(new TenantId(Company)), default))
            .Should().BeEmpty();

        ledger.Questions.Should().Be(before + 2);
    }

    /// <summary>An evaluation comes into being only together with its note.</summary>
    /// <remarks>
    /// Without the note the person would learn of a reply only at their next
    /// visit. Both stand in <em>one</em> transaction (<c>TransactionBehavior</c>);
    /// that both happen at all is what this test measures.
    /// </remarks>
    [Fact]
    public async Task An_evaluation_notes_that_the_person_is_to_read_it()
    {
        var (cases, ledger, outbox, clock) = Setup();
        var caseItem = await SetTask(cases, ledger, clock);

        caseItem.Submit(Submission.Write("Done.", null, clock.GetUtcNow()), clock.GetUtcNow());
        outbox.Notes.Clear();

        await new EvaluateHandler(cases, ledger, outbox, clock).Handle(
            new EvaluateCommand(caseItem.Id, new TenantId(Company), Outcome.Rejected, "Too thin."),
            default);

        outbox.Notes.Should().ContainSingle()
            .Which.Should().Be((Anna, NotificationKinds.Moved));
    }

    /// <summary>It is evaluated once.</summary>
    /// <remarks>
    /// An evaluation that can be changed afterwards is one the person may have
    /// read before it got its final wording — and what they read would then no
    /// longer be provable.
    /// </remarks>
    [Fact]
    public void It_is_evaluated_once()
    {
        var caseItem = Case.Open(
            new SubjectId(Anna), new TenantId(Company), SampleAssignment(), Now);

        caseItem.Submit(Submission.Write("Done.", null, Now), Now);
        caseItem.Evaluate(Evaluation.Write("Good.", Outcome.Accepted, Now), Now);

        FluentActions.Invoking(() =>
                caseItem.Evaluate(Evaluation.Write("On second thoughts, no.", Outcome.Rejected, Now), Now))
            .Should().Throw<StepNotPossible>();
    }

    /// <summary>Without a submission there is nothing to evaluate.</summary>
    /// <remarks>
    /// An evaluation without work would be a statement about the human being
    /// instead of about their work.
    /// </remarks>
    [Fact]
    public void Without_a_submission_there_is_nothing_to_evaluate() =>
        FluentActions.Invoking(() =>
                Case.Open(new SubjectId(Anna), new TenantId(Company), SampleAssignment(), Now)
                    .Evaluate(Evaluation.Write("Good.", Outcome.Accepted, Now), Now))
            .Should().Throw<StepNotPossible>();

    // ---------------------------------------------------------------------
    // CONDITION 3 — THE SIZE STANDS UP FRONT, DECLINING IS RECORDED NOWHERE
    // ---------------------------------------------------------------------

    /// <summary>A size outside one to eight is no work sample.</summary>
    /// <remarks>
    /// More than one working day is no longer a sample but work — and for work
    /// there is a contract and not a form. The platform simply offers no field for
    /// "sixteen hours".
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(9)]
    [InlineData(40)]
    public void A_size_outside_the_limits_is_refused(int hours) =>
        FluentActions.Invoking(() => Assignment.Write(
                "Small service", "Build something small.", hours, Now.AddDays(7), Now))
            .Should().Throw<InputError>();

    /// <summary>The counter-probe: eight works, nine does not.</summary>
    [Fact]
    public void Eight_hours_just_pass()
    {
        Assignment.Write("Small service", "Build something small.", 8, Now.AddDays(7), Now)
            .Hours.Should().Be(8);

        Assignment.MaxHours.Should().Be(8, "one working day is the limit, not a starting value");
    }

    /// <summary>A deadline under two days is refused.</summary>
    /// <remarks>
    /// A task for tomorrow morning does not measure how somebody works but whether
    /// they can drop everything else right now — and that has nothing to do with
    /// ability and everything to do with living circumstances.
    /// </remarks>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(12)]
    [InlineData(47)]
    public void A_too_short_deadline_is_refused(int hours) =>
        FluentActions.Invoking(() => Assignment.Write(
                "Small service", "Build something small.", 4, Now.AddHours(hours), Now))
            .Should().Throw<InputError>();

    /// <summary>And the counter-probe at forty-eight.</summary>
    [Fact]
    public void Forty_eight_hours_just_pass() =>
        Assignment.Write("Small service", "Build something small.", 4, Now.AddHours(48), Now)
            .Deadline.Should().Be(Now.AddHours(48));

    /// <summary>The message names the rule and never the value.</summary>
    /// <remarks>
    /// <c>ValidationBehavior</c> logs error messages. A task text or an evaluation
    /// in a message would thus stand in the log — and both are about a human
    /// being.
    /// </remarks>
    [Fact]
    public void A_message_names_the_rule_and_never_the_value()
    {
        var error = Assert.Throws<InputError>(() => Assignment.Write(
            "Secret title", "Secret task text", 4, Now.AddHours(1), Now));

        error.Message.Should().NotContain("Secret");
    }

    /// <summary>A state knows four values, and none of them is called "declined".</summary>
    /// <remarks>
    /// <strong>The third condition as a type probe.</strong> Whoever does not want
    /// to does nothing — there is no state in which that would be recorded.
    /// </remarks>
    [Fact]
    public void No_state_is_called_declined() =>
        Enum.GetNames<State>().Should().BeEquivalentTo(
            "Set", "Submitted", "Evaluated", "Expired");

    /// <summary>And nowhere is there a way to decline.</summary>
    /// <remarks>
    /// <para>The application layer and the domain are scanned: that is where the
    /// command would stand. A polite decline button would be kinder to the company
    /// and would create exactly the record ADR-0042 §3 forbids — "declined three
    /// times" is a fact about a human being, it arises from nothing but
    /// individually reasonable clicks, and afterwards it is there.</para>
    ///
    /// <para><c>Outcome.Rejected</c> is something else and is therefore the one
    /// name let through: it is the company's answer to the <em>work</em>, not the
    /// person's decision about the <em>task</em>. It is let through by its full
    /// name, so a <c>State.Rejected</c> or a <c>RejectTaskCommand</c> still falls
    /// here. The German words stay on the list because they catch a German name
    /// slipping back in.</para>
    /// </remarks>
    [Theory]
    [InlineData("decline")]
    [InlineData("reject")]
    [InlineData("refuse")]
    [InlineData("ablehnen")]
    [InlineData("absagen")]
    [InlineData("verweiger")]
    public void There_is_no_way_to_decline(string word)
    {
        string[] names =
        [
            .. Names(typeof(SetTaskCommand).Assembly),
            .. Names(typeof(Case).Assembly)
        ];

        names.Where(name => name != "Outcome.Rejected")
            .Should().NotContain(
                name => name.Contains(word, StringComparison.OrdinalIgnoreCase),
                $"'{word}' would be the record ADR-0042 §3 rules out");
    }

    /// <summary>Expired is expired — no matter why.</summary>
    /// <remarks>
    /// "Did not want to", "did not manage it" and "was ill" are the same state:
    /// <c>expired</c>. There is no field that tells them apart, and therefore no
    /// information about it either.
    /// </remarks>
    [Fact]
    public void Expired_is_expired()
    {
        var caseItem = Case.Open(
            new SubjectId(Anna), new TenantId(Company), SampleAssignment(), Now);

        caseItem.StateAt(Now).Should().Be(State.Set);
        caseItem.StateAt(Now.AddDays(8)).Should().Be(State.Expired);

        // And afterwards nothing goes in any more: a task without an end is one
        // that stays open in somebody's head for ever.
        FluentActions.Invoking(() => caseItem.Submit(
                Submission.Write("After all.", null, Now.AddDays(8)), Now.AddDays(8)))
            .Should().Throw<StepNotPossible>();
    }

    /// <summary>Whoever delivered does not count as expired.</summary>
    /// <remarks>
    /// The order of the questions in <c>StateAt</c> is the statement: an expired
    /// deadline does not undo a submitted case, and the company still owes the
    /// answer afterwards.
    /// </remarks>
    [Fact]
    public void Whoever_delivered_does_not_expire()
    {
        var caseItem = Case.Open(
            new SubjectId(Anna), new TenantId(Company), SampleAssignment(), Now);

        caseItem.Submit(Submission.Write("Done.", null, Now), Now);

        caseItem.StateAt(Now.AddDays(8)).Should().Be(State.Submitted);
    }

    // ---------------------------------------------------------------------
    // WHAT THIS SERVICE DOES NOT DO
    // ---------------------------------------------------------------------

    /// <summary>There is no AI seam — and expressly none for the evaluation.</summary>
    /// <remarks>
    /// A model judging a human being's work would be ADR-0022's black box with a
    /// decimal point, only in prose. There is no port for it in this service, and
    /// so no question either of what a model would be allowed to say.
    /// <para>
    /// <c>model</c> is not on the list, and that is no oversight: EF names the
    /// migration snapshot <c>AssessmentDbContextModelSnapshot</c>. A word that
    /// means two things is no use as a guard — what is searched for is the
    /// <em>port</em>, not the syllable.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("drafter")]
    [InlineData("draft")]
    [InlineData("prompt")]
    public void There_is_no_AI_seam(string word)
    {
        string[] names =
        [
            .. Names(typeof(SetTaskCommand).Assembly),
            .. Names(typeof(Case).Assembly),
            .. Names(typeof(AssessmentDbContext).Assembly)
        ];

        names.Should().NotContain(
            name => name.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Exactly two doors lead outside, and both are named.</summary>
    /// <remarks>
    /// <para>The ledger and notification-service — both stand in
    /// <c>docker-compose.yml</c>, because the egress boundary derives its allowed
    /// hosts from the configuration.</para>
    ///
    /// <para><strong>The submitted address is not one of them.</strong> It is
    /// stored and shown, never fetched. A third type with an
    /// <c>IHttpClientFactory</c> would be the first suspect — and this test forces
    /// whoever adds it to write it down.</para>
    /// </remarks>
    [Fact]
    public void Exactly_two_doors_lead_outside() =>
        typeof(AssessmentDbContext).Assembly.GetExportedTypes()
            .Where(type => type.GetConstructors().Any(constructor => constructor.GetParameters()
                .Any(parameter => parameter.ParameterType == typeof(IHttpClientFactory))))
            .Select(type => type.Name)
            .Should().BeEquivalentTo("HttpConsentGate", "HttpNotifier");

    /// <summary>An address is http or https — otherwise it is none.</summary>
    /// <remarks>
    /// <c>javascript:</c> and <c>data:</c> would otherwise stand as a link in
    /// somebody else's interface: stored by one human being, clicked by another.
    /// </remarks>
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>")]
    [InlineData("file:///etc/passwd")]
    [InlineData("not even an address")]
    public void An_address_is_http_or_nothing(string address) =>
        FluentActions.Invoking(() => Submission.Write(null, address, Now))
            .Should().Throw<InputError>();

    /// <summary>The counter-probe.</summary>
    [Fact]
    public void An_http_address_gets_through() =>
        Submission.Write(null, "https://example.test/solution", Now)
            .Url.Should().Be("https://example.test/solution");

    /// <summary>An empty submission is none at all.</summary>
    [Fact]
    public void An_empty_submission_is_none() =>
        FluentActions.Invoking(() => Submission.Write("  ", null, Now))
            .Should().Throw<InputError>();

    // ---------------------------------------------------------------------

    /// <summary>A type's public instance properties.</summary>
    private static string[] Fields(Type type) =>
        [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(member => member.Name != "EqualityContract")
            .Select(member => member.Name)];

    private static string[] Names(Assembly assembly) =>
        [.. assembly.GetExportedTypes()
            .SelectMany(type => type.GetMembers(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.DeclaredOnly)
                .Select(member => $"{type.Name}.{member.Name}")
                .Append(type.Name))
            .Distinct(StringComparer.Ordinal)];

    /// <summary>Every column this model really creates.</summary>
    private static string[] Columns()
    {
        var builder = new DbContextOptionsBuilder<AssessmentDbContext>();
        AssessmentDbContextFactory.AtDesignTime(builder, "Host=test-bench;Database=assessment");

        using var context = new AssessmentDbContext(builder.Options);

        return [.. context.Model.GetEntityTypes()
            .SelectMany(type => type.GetProperties())
            .Select(property => property.GetColumnName())
            .Distinct(StringComparer.Ordinal)];
    }

    private static Assignment SampleAssignment() =>
        Assignment.Write(
            "Small service", "Build a small service.", 4, Now.AddDays(7), Now);

    private static (ProbeCases, ProbeLedger, ProbeOutbox, ProbeClock) Setup() =>
        (new ProbeCases(), new ProbeLedger(), new ProbeOutbox(), new ProbeClock(Now));

    private static async Task<Case> SetTask(
        ProbeCases cases, ProbeLedger ledger, ProbeClock clock)
    {
        ledger.Set(Anna, Company, true);

        return await new SetTaskHandler(cases, ledger, new ProbeOutbox(), clock).Handle(
            new SetTaskCommand(
                new SubjectId(Anna),
                new TenantId(Company),
                "Small service",
                "Build a small service.",
                4,
                clock.GetUtcNow().AddDays(7)),
            default);
    }
}
