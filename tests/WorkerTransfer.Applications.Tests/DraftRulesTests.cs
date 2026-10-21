using FluentAssertions;
using Noelia.Core.Identity;
using WorkerTransfer.Applications.Application.Drafts;
using WorkerTransfer.Applications.Application.Ports;
using WorkerTransfer.Applications.Domain.Applications;
using WorkerTransfer.Applications.Infrastructure.CoverLetter;

namespace WorkerTransfer.Applications.Tests;

/// <summary>The rules from ADR-0034 — as checks, not as a declaration of intent.</summary>
public sealed class DraftRulesTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    private static ApplicationDraft Begun() =>
        ApplicationDraft.Begin(
            Guid.CreateVersion7(), new TenantId(Guid.CreateVersion7()),
            new SubjectId(Guid.CreateVersion7()), Now);

    private static ApplicationDraft Written()
    {
        var draft = Begun();

        draft.StartWriting(Now);
        draft.AcceptText("Application as a developer", "Dear Sir or Madam …", Now);

        return draft;
    }

    /// <summary>
    /// <strong>The model never starts by itself.</strong> A writing costs the
    /// person's AI credit, so a draft begins open and waits for her choice.
    /// </summary>
    [Fact]
    public void A_draft_begins_open_and_nobody_writes()
    {
        var draft = Begun();

        draft.State.Should().Be(DraftState.Open);
        draft.WritingStartedAt.Should().BeNull();
    }

    /// <summary>From open, the person may write the letter herself.</summary>
    [Fact]
    public void From_open_one_writes_it_oneself()
    {
        var draft = Begun();

        draft.EditByAuthor("Application", "I write it myself.", Now);

        draft.State.Should().Be(DraftState.Review);
        draft.Text.Should().Be("I write it myself.");
    }

    /// <summary>From open, the model may write — on her click.</summary>
    [Fact]
    public void From_open_the_model_writes_on_request()
    {
        var draft = Begun();

        draft.StartWriting(Now);

        draft.State.Should().Be(DraftState.Generating);
        draft.WritingStartedAt.Should().Be(Now);
    }

    /// <summary>An open draft has no text — no comment, no approval, no model answer.</summary>
    [Fact]
    public void An_open_draft_takes_no_comment_and_no_model_answer()
    {
        var draft = Begun();

        var comment = () => draft.AddComment("Shorter.", "Dear", Now);
        var answer = () => draft.AcceptText("Application", "Dear Sir.", Now);
        var approve = () => draft.Approve(Now);

        comment.Should().Throw<DraftStepNotAllowed>();
        answer.Should().Throw<DraftStepNotAllowed>();
        approve.Should().Throw<DraftStepNotAllowed>();
    }

    /// <summary>
    /// <strong>There is no way from "generating" to "sent".</strong>
    /// </summary>
    /// <remarks>
    /// Between the model and the company stand two acts of a human being:
    /// approving and sending. A button "approve and send" saves a click and takes
    /// the approval's meaning away — it is the moment in which somebody says
    /// "like this, and not otherwise".
    /// </remarks>
    [Fact]
    public void Nothing_goes_out_without_approval()
    {
        var draft = Written();

        var attempt = () => draft.MarkSent(Now);

        attempt.Should().Throw<DraftStepNotAllowed>();
        draft.State.Should().Be(DraftState.Review);
    }

    /// <summary>And certainly not from "generating".</summary>
    [Fact]
    public void Nobody_approves_while_it_is_being_generated()
    {
        var draft = Begun();

        var approve = () => draft.Approve(Now);
        var send = () => draft.MarkSent(Now);

        approve.Should().Throw<DraftStepNotAllowed>();
        send.Should().Throw<DraftStepNotAllowed>();
    }

    /// <summary>An open comment prevents the approval.</summary>
    [Fact]
    public void Nobody_approves_with_an_open_comment()
    {
        var draft = Written();

        draft.AddComment("The second paragraph sounds hollow.", "Dear Sir", Now);

        var attempt = () => draft.Approve(Now);

        attempt.Should().Throw<CommentConflict>();
        draft.State.Should().Be(DraftState.NeedsChanges);
    }

    /// <summary>
    /// <strong>Without an instruction nothing is revised.</strong>
    /// </summary>
    /// <remarks>
    /// ADR-0024 forbids the reflection loop — "two calls to rewrite the person's
    /// own words <em>unasked</em>". A button that rewrites without an open
    /// comment would be exactly that under another name.
    /// </remarks>
    [Fact]
    public void Nothing_is_revised_without_a_comment()
    {
        var draft = Written();

        var attempt = () => draft.Revise("New", "Completely different", Now);

        attempt.Should().Throw<CommentConflict>();
        draft.Text.Should().Be("Dear Sir or Madam …");
        draft.Version.Should().Be(1);
    }

    /// <summary>The revision ticks off and counts up.</summary>
    /// <remarks>
    /// The version number is no ornament: whoever does not see that something
    /// changed does not really check.
    /// </remarks>
    [Fact]
    public void The_revision_ticks_off_and_counts_up()
    {
        var draft = Written();

        draft.AddComment("Shorter.", null, Now);
        draft.Revise("Application", "Short and good.", Now);

        draft.State.Should().Be(DraftState.Review);
        draft.Version.Should().Be(2);
        draft.HasOpenComments.Should().BeFalse();

        // The comment STAYS — it is the evidence for why the text changed.
        // Deleting it would rob the version history of its reason.
        draft.Comments.Should().HaveCount(1);
        draft.Comments[0].Resolved.Should().BeTrue();
    }

    /// <summary>A comment after the approval takes it back.</summary>
    /// <remarks>
    /// Whoever still finds something after the approval has found it. An
    /// approval that survives an objection is none.
    /// </remarks>
    [Fact]
    public void A_comment_after_the_approval_takes_it_back()
    {
        var draft = Written();

        draft.Approve(Now);
        draft.State.Should().Be(DraftState.Approved);

        draft.AddComment("Not after all.", null, Now);

        draft.State.Should().Be(DraftState.NeedsChanges);

        var attempt = () => draft.MarkSent(Now);
        attempt.Should().Throw<DraftStepNotAllowed>();
    }

    /// <summary>Changing it oneself takes the approval back as well.</summary>
    /// <remarks>
    /// Otherwise "approved" would say nothing about the text that then goes out.
    /// </remarks>
    [Fact]
    public void An_own_change_after_the_approval_takes_it_back()
    {
        var draft = Written();

        draft.Approve(Now);
        draft.EditByAuthor("Application", "I changed something else.", Now);

        draft.State.Should().Be(DraftState.Review);
    }

    /// <summary>Nobody approves an empty cover letter.</summary>
    [Fact]
    public void An_empty_cover_letter_does_not_get_through()
    {
        var draft = Begun();

        draft.StartWriting(Now);
        draft.AcceptText("Subject", "", Now);

        var attempt = () => draft.Approve(Now);

        attempt.Should().Throw<DraftInputError>();
    }

    /// <summary>
    /// After a failed writing the person may write herself.
    /// </summary>
    /// <remarks>
    /// From <c>Generating</c>, <c>EditByAuthor</c> is forbidden. Without a
    /// provider the draft must therefore fall to <c>Failed</c> — otherwise the
    /// row stays "being written" for ever and the letter stays empty.
    /// </remarks>
    [Fact]
    public void After_a_failed_writing_one_may_change_it_oneself()
    {
        var draft = Begun();

        draft.Fail(CoverLetterUnavailable.NoProvider, Now);
        draft.EditByAuthor("Application", "I write it myself.", Now);

        draft.State.Should().Be(DraftState.Review);
        draft.Text.Should().Be("I write it myself.");
    }

    /// <summary>
    /// After a failure the model may fill the same draft once more — otherwise a
    /// successful answer is thrown away as a 422.
    /// </summary>
    [Fact]
    public void After_a_failure_the_model_may_write_again()
    {
        var draft = Begun();

        draft.Fail("The draft provider answers with 404.", Now);
        draft.AcceptText("Application", "Dear Sir or Madam.", Now);

        draft.State.Should().Be(DraftState.Review);
        draft.Text.Should().Be("Dear Sir or Madam.");
        draft.Error.Should().BeEmpty();
    }

    /// <summary>
    /// A cover letter stays short: 1500 tokens pushed a local model beyond the
    /// minute, and nobody waits longer than a minute.
    /// </summary>
    [Fact]
    public void The_cover_letter_asks_for_no_long_answer()
    {
        HttpCoverLetterWriter.MaxTokens.Should().BeLessThanOrEqualTo(1500);
        // The prompt is German on purpose (see CoverLetterContext.Rules); these
        // are its words.
        CoverLetterContext.Rules.Should().Contain("250");
        CoverLetterContext.Rules.Should().Contain("Muttersprachler");
        CoverLetterContext.Rules.Should().Contain("Erfinde");
        CoverLetterContext.Rules.Should().Contain("Signatur");
    }

    /// <summary>
    /// The reason a failure leaves behind is read by the interface, by its words.
    /// </summary>
    /// <remarks>
    /// <c>web/src/features/work/lib/draftErrors.ts</c> recognises "nothing set
    /// up" by <c>/no writing-help provider/i</c> (or the German sentence it
    /// replaced). A reworded constant would silently turn "set up a provider"
    /// into "the provider answers".
    /// </remarks>
    [Fact]
    public void The_no_provider_reason_is_the_one_the_interface_recognises()
    {
        CoverLetterUnavailable.NoProvider
            .Should().MatchRegex("(?i)no writing-help provider");
    }

    [Fact]
    public void A_fragment_stays_generating()
    {
        var draft = Begun();

        draft.StartWriting(Now);
        var start = draft.WritingStartedAt;

        draft.TakeFragment("Application", "Dear Sir", Now.AddSeconds(12));

        draft.State.Should().Be(DraftState.Generating);
        draft.Text.Should().Be("Dear Sir");
        draft.Version.Should().Be(1);
        draft.WritingStartedAt.Should().Be(start);
    }

    [Fact]
    public void The_first_writing_sets_the_start()
    {
        var draft = Begun();

        draft.WritingStartedAt.Should().BeNull();

        draft.StartWriting(Now, clear: false);

        draft.WritingStartedAt.Should().Be(Now);
        draft.State.Should().Be(DraftState.Generating);
    }

    [Fact]
    public void After_a_failure_the_writing_begins_again()
    {
        var draft = Begun();

        draft.Fail("timeout", Now);
        draft.StartWriting(Now);

        draft.State.Should().Be(DraftState.Generating);
        draft.Error.Should().BeEmpty();
        draft.Text.Should().BeEmpty();
    }

    /// <summary>The whole way, with every jump as an act of its own.</summary>
    [Fact]
    public void The_whole_way_demands_every_step_on_its_own()
    {
        var draft = Written();

        draft.AddComment("Please be more concrete.", "Dear Sir", Now);
        draft.State.Should().Be(DraftState.NeedsChanges);

        draft.Revise("Application", "Concrete.", Now);
        draft.State.Should().Be(DraftState.Review);

        draft.Approve(Now);
        draft.State.Should().Be(DraftState.Approved);

        draft.MarkSent(Now);
        draft.State.Should().Be(DraftState.Sent);

        // Then it is over: nobody changes a sent cover letter any more.
        var afterwards = () => draft.EditByAuthor("x", "y", Now);
        afterwards.Should().Throw<DraftStepNotAllowed>();
    }

    /// <summary>
    /// "This step is not possible now" stays recognisable to the interface.
    /// </summary>
    /// <remarks>
    /// <c>schreibfehler.ts</c> recognises it by the German "kann nicht
    /// (geschrieben|geändert|überarbeitet) werden" and has no English
    /// alternative; that is why <see cref="DraftStepNotAllowed"/> keeps its
    /// German sentence.
    /// </remarks>
    [Fact]
    public void A_step_that_is_not_possible_stays_recognisable_to_the_interface()
    {
        var draft = Written();
        draft.Approve(Now);
        draft.MarkSent(Now);

        var write = () => draft.StartWriting(Now);
        var change = () => draft.EditByAuthor("x", "y", Now);
        var revise = () => draft.Revise("x", "y", Now);

        write.Should().Throw<DraftStepNotAllowed>()
            .Which.Message.Should().MatchRegex("kann nicht geschrieben werden");
        change.Should().Throw<DraftStepNotAllowed>()
            .Which.Message.Should().MatchRegex("kann nicht geändert werden");
        revise.Should().Throw<DraftStepNotAllowed>()
            .Which.Message.Should().MatchRegex("kann nicht überarbeitet werden");
    }

    /// <summary>
    /// <strong>The context carries only one's own details and the advert.</strong>
    /// </summary>
    /// <remarks>
    /// The same check ADR-0024 provides for <c>AdvertDraft</c> and the profile
    /// draft: the field list is the boundary. Whoever adds a field here answers
    /// first — does it belong to the applying person, or does it stand in the
    /// public advert? Everything else would be a statement about a third party.
    /// </remarks>
    [Fact]
    public void The_context_carries_nothing_about_third_parties()
    {
        typeof(CoverLetterContext).GetProperties()
            .Select(property => property.Name)
            .Should().BeEquivalentTo(
                // The advert — publicly readable, without signing in.
                nameof(CoverLetterContext.JobTitle),
                nameof(CoverLetterContext.Company),
                nameof(CoverLetterContext.JobLocation),
                nameof(CoverLetterContext.JobDescription),
                nameof(CoverLetterContext.RequestedSkills),
                // One's own details.
                nameof(CoverLetterContext.OwnName),
                nameof(CoverLetterContext.OwnHeadline),
                nameof(CoverLetterContext.OwnText),
                nameof(CoverLetterContext.OwnSkills),
                nameof(CoverLetterContext.OwnCareer),
                nameof(CoverLetterContext.Language));
    }

    /// <summary>
    /// Postal address, mail and phone belong on the letterhead, not in the model
    /// (ADR-0038). The allow-list above is not enough: a field
    /// <c>PostalAddress</c> in place of <c>OwnHeadline</c> would only shift the
    /// list.
    /// </summary>
    /// <remarks>
    /// Every German word stands beside its English one: since the code is
    /// English, a list with only <c>gehalt</c> would let a <c>Salary</c> through.
    /// </remarks>
    [Theory]
    [InlineData("anschrift")]
    [InlineData("address")]
    [InlineData("email")]
    [InlineData("telefon")]
    [InlineData("phone")]
    [InlineData("gehalt")]
    [InlineData("salary")]
    public void The_context_carries_no_postal_address(string word)
    {
        typeof(CoverLetterContext).GetProperties()
            .Select(property => property.Name.ToLowerInvariant())
            .Should().NotContain(name => name.Contains(word, StringComparison.Ordinal));
    }

    /// <summary>And no number about a human being, not even one's own.</summary>
    /// <remarks>
    /// ADR-0022 at the place where it is easiest to overlook: "I am 90 %
    /// suitable" would be a number about a human being, only from their own pen.
    /// </remarks>
    [Theory]
    [InlineData("score")]
    [InlineData("rank")]
    [InlineData("weight")]
    [InlineData("fit")]
    [InlineData("percent")]
    public void No_forbidden_word_on_the_draft(string word)
    {
        var names = typeof(ApplicationDraft).GetProperties()
            .Select(property => property.Name.ToLowerInvariant())
            .Concat(typeof(CoverLetterContext).GetProperties()
                .Select(property => property.Name.ToLowerInvariant()));

        names.Should().NotContain(name => name.Contains(word, StringComparison.Ordinal));
    }

    /// <summary>The output format is read, not guessed.</summary>
    /// <remarks>
    /// A parser that only knows the ideal case turns a slight deviation into an
    /// empty cover letter — and an empty cover letter in the state "review" is an
    /// invitation to approve it by mistake. The marker is the German
    /// <c>BETREFF:</c> the prompt asks for.
    /// </remarks>
    [Fact]
    public void The_output_format_survives_deviations()
    {
        CoverLetterFormat.Parse("BETREFF: Application\n---\nDear Sir …")
            .Should().Be(("Application", "Dear Sir …"));

        // Without a dash line.
        CoverLetterFormat.Parse("BETREFF: Application\nDear Sir …")
            .Should().Be(("Application", "Dear Sir …"));

        // With a Markdown fence and asterisks.
        CoverLetterFormat.Parse("```\nBETREFF: **Application**\n---\nDear Sir …\n```")
            .Should().Be(("Application", "Dear Sir …"));

        // Without any marker: then everything is the text, and the subject line
        // stays empty — empty and not invented.
        CoverLetterFormat.Parse("Dear Sir …")
            .Should().Be((string.Empty, "Dear Sir …"));

        // An unfinished subject line: show the raw text, otherwise the letter
        // stays empty while the model is still typing the subject.
        CoverLetterFormat.Parse("BETREFF: Application as")
            .Should().Be((string.Empty, "BETREFF: Application as"));
    }
}
