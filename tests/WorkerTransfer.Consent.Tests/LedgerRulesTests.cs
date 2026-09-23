using FluentAssertions;
using Noelia.Core.Identity;
using WorkerTransfer.Consent.Domain.Ledger;

namespace WorkerTransfer.Consent.Tests;

/// <summary>The rules of the log, without a database.</summary>
public class LedgerRulesTests
{
    private static readonly SubjectId Anna = new(Guid.CreateVersion7());
    private static readonly Capability Visibility = Capability.Parse("profile.visibility:public");
    private static readonly DateTimeOffset Now = new(2026, 8, 22, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("profile.visibility:public")]
    [InlineData("portfolio.visibility:public")]
    [InlineData("resume.visibility:tenant:9f8e7d6c-5b4a-4938-8271-605f4e3d2c1b")]
    [InlineData("profile.visibility")]
    public void A_capability_reads(string value) =>
        Capability.TryParse(value, out _).Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("Profile.Visibility:public")]
    [InlineData("p")]
    [InlineData(" profile.visibility:public")]
    [InlineData("profile visibility")]
    public void And_what_is_not_one_does_not_read(string value) =>
        Capability.TryParse(value, out _).Should().BeFalse();

    [Fact]
    public void A_capability_that_is_too_long_is_refused() =>
        Capability.TryParse("a." + new string('b', Capability.MaxLength), out _)
            .Should().BeFalse();

    /// <summary>
    /// The vocabulary is not checked, only the shape.
    /// </summary>
    /// <remarks>
    /// A list of permitted capabilities would be a claim about which
    /// permissions can exist, and consuming services have to be able to ask
    /// about anything — absence is a state, not an error.
    /// </remarks>
    [Fact]
    public void An_unknown_but_well_formed_capability_is_accepted() =>
        Capability.TryParse("doesnotexistyet.visibility:public", out _).Should().BeTrue();

    [Fact]
    public void A_reason_must_not_be_empty()
    {
        WithdrawalReason.TryParse("   ", out _).Should().BeFalse();
        WithdrawalReason.TryParse(null, out _).Should().BeFalse();
    }

    [Fact]
    public void A_reason_must_not_be_longer_than_five_hundred_characters()
    {
        WithdrawalReason.TryParse(new string('x', 500), out _).Should().BeTrue();
        WithdrawalReason.TryParse(new string('x', 501), out _).Should().BeFalse();
    }

    /// <summary>Withdrawing must always be explainable; granting need not be.</summary>
    [Fact]
    public void A_revocation_without_a_reason_does_not_come_about()
    {
        var withoutReason = () => ConsentEvent.Restore(
            Guid.CreateVersion7(), Anna, Visibility, ConsentAction.Revoke, Now, Anna, null,
            null);

        // Restoring one is fine — see the next test. Recording one is not, and
        // the signature of Revoke() is what says so.
        withoutReason.Should().NotThrow();
        typeof(ConsentEvent).GetMethod(nameof(ConsentEvent.Revoke))!
            .GetParameters().Should().Contain(p => p.Name == "reason" && !p.IsOptional);
    }

    /// <summary>
    /// After an erasure a withdrawal legitimately has no reason left.
    /// </summary>
    /// <remarks>
    /// ADR-0027 §5 clears the free text of every row of that person, a
    /// withdrawal included. Refusing to read such a row back would make the
    /// erasure the thing that breaks the ledger — and the ledger is what proves
    /// the erasure happened.
    /// </remarks>
    [Fact]
    public void A_restored_revocation_without_a_reason_is_no_error()
    {
        var restoredEvent = ConsentEvent.Restore(
            Guid.CreateVersion7(), Anna, Visibility, ConsentAction.Revoke, Now, Anna, null,
            null);

        restoredEvent.Reason.Should().BeNull();
        Projection.State([restoredEvent]).Granted.Should().BeFalse();
    }

    [Fact]
    public void An_erasure_demands_no_reason() =>
        ConsentEvent.Delete(Anna, Visibility, Now, Anna).Reason.Should().BeNull();

    [Fact]
    public void Foreign_metadata_does_not_get_in()
    {
        var withEmail = () => ConsentEvent.Grant(
            Anna, Visibility, Now, Anna,
            metadata: new Dictionary<string, string> { ["email"] = "anna@example.com" });

        withEmail.Should().Throw<ConsentMetadataException>().Which.Key.Should().Be("email");
    }

    [Fact]
    public void No_event_means_not_granted()
    {
        Projection.State([]).Granted.Should().BeFalse();
        Projection.State([]).Reason.Should().Be("no consent event");
        Projection.State([]).Deleted.Should().BeFalse();
    }

    [Fact]
    public void The_newest_event_decides()
    {
        var granted = ConsentEvent.Grant(Anna, Visibility, Now, Anna);
        var revoked = ConsentEvent.Revoke(
            Anna, Visibility, Now.AddMinutes(1), WithdrawalReason.Parse("I no longer want this."),
            Anna);

        Projection.State([granted, revoked]).Granted.Should().BeFalse();
        Projection.State([revoked, granted]).Granted.Should().BeFalse();
    }

    /// <summary>A withdrawal must never be a one-way door.</summary>
    [Fact]
    public void After_a_revocation_it_may_be_granted_again()
    {
        var revoked = ConsentEvent.Revoke(
            Anna, Visibility, Now, WithdrawalReason.Parse("Not for now."), Anna);
        var again = ConsentEvent.Grant(Anna, Visibility, Now.AddDays(30), Anna);

        Projection.State([revoked, again]).Should().Be(new ConsentState(true));
    }

    /// <summary>
    /// Two facts in the same clock tick resolve deterministically.
    /// </summary>
    /// <remarks>
    /// Without the tie-break the answer would depend on the order rows came
    /// back in, and the same pair could read differently twice in a row.
    /// </remarks>
    [Fact]
    public void Two_events_in_the_same_tick_resolve_over_the_id()
    {
        var smallerId = Guid.Parse("00000000-0000-7000-8000-000000000001");
        var largerId = Guid.Parse("00000000-0000-7000-8000-000000000002");

        var granted = ConsentEvent.Restore(
            largerId, Anna, Visibility, ConsentAction.Grant, Now, Anna, null, null);
        var revoked = ConsentEvent.Restore(
            smallerId, Anna, Visibility, ConsentAction.Revoke, Now, Anna,
            WithdrawalReason.Parse("Changed my mind."), null);

        Projection.State([granted, revoked]).Granted.Should().BeTrue();
        Projection.State([revoked, granted]).Granted.Should().BeTrue();
    }

    [Fact]
    public void A_revocation_carries_its_reason_into_the_state()
    {
        var revoked = ConsentEvent.Revoke(
            Anna, Visibility, Now, WithdrawalReason.Parse("New job."), Anna);

        Projection.State([revoked]).Should()
            .Be(new ConsentState(false, false, "New job."));
    }

    [Fact]
    public void An_erasure_is_not_the_same_as_a_revocation()
    {
        var deleted = ConsentEvent.Delete(Anna, Visibility, Now, Anna);

        Projection.State([deleted]).Should().Be(new ConsentState(false, true));
    }

    /// <summary>
    /// Append-only is the shape of the port, not a rule in a document.
    /// </summary>
    [Fact]
    public void The_ledger_offers_no_update_and_no_delete() =>
        typeof(IConsentLedger).GetMethods().Select(m => m.Name).Should()
            .NotContain(name =>
                name.Contains("Update", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Aender", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Delete", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Loesch", StringComparison.OrdinalIgnoreCase));
}
