using FluentAssertions;
using Noelia.Core.Logging;
using Noelia.Infrastructure.Logging;
using Serilog.Events;
using Serilog.Parsing;

namespace WorkerTransfer.Ganzes.Tests;

/// <summary>What Noelia's masking redacts in 4.4.0 — and what it releases again.</summary>
/// <remarks>
/// <para><strong>This suite is the counter-probe to a zero measurement.</strong>
/// Nowhere in the running stack does <c>[REDACTED]</c> stand, and that has two
/// possible meanings: there is nothing to mask, or the masking does not run.
/// Without these tests the first reading would be a hope.</para>
///
/// <para><strong>4.4.0 turns the direction in two places</strong>, and both are
/// pinned here: the comparison is now <em>exact</em> rather than by substring,
/// which makes <c>SecretName</c> and <c>TokenId</c> readable again — the name of
/// a secret is not one, and without it an operator cannot say which one was
/// meant. In return <c>Username</c>, <c>Email</c> and <c>City</c> now fall under
/// it, which they did not before.</para>
///
/// <para>For WorkerTransfer both are without consequence, and that too is
/// measured: our log templates carry German names, and Noelia's
/// <c>LoggingBehavior</c> writes <em>shapes</em> anyway —
/// <c>Email: string(34)</c> is a length, never an address. There is nothing here
/// that could go dark.</para>
/// </remarks>
public sealed class MaskierungTests
{
    /// <summary>An event with exactly one property, through the enricher.</summary>
    private static string Angereichert(string name, string value)
    {
        var ereignis = new LogEvent(
            DateTimeOffset.UnixEpoch,
            LogEventLevel.Information,
            exception: null,
            new MessageTemplate("egal", []),
            [new LogEventProperty(name, new ScalarValue(value))]);

        new DataMaskingEnricher().Enrich(ereignis, propertyFactory: null!);

        return ereignis.Properties[name] is ScalarValue { Value: string gelesen }
            ? gelesen
            : ereignis.Properties[name].ToString().Trim('"');
    }

    /// <summary>What is redacted from 4.4.0 on — and was visible before.</summary>
    [Theory]
    [InlineData("Username")]
    [InlineData("Email")]
    [InlineData("City")]
    public void Newly_redacted(string name)
    {
        Angereichert(name, "etwas Persönliches").Should().Be(DataMaskingEnricher.Mask);
    }

    /// <summary>
    /// <strong>And what 4.4.0 releases again.</strong>
    /// </summary>
    /// <remarks>
    /// In 4.3.0 the substring "secret" also hit <c>SecretName</c> and "token"
    /// also hit <c>TokenId</c>. That was the bug 4.4.0 fixes: a log in which both
    /// are redacted is one nobody can follow any more — you then know that a
    /// secret was used, but not which one.
    /// </remarks>
    [Theory]
    [InlineData("SecretName", "jwt-signing-key")]
    [InlineData("TokenId", "01a070e2-847e-78f7")]
    public void Visible_again(string name, string value)
    {
        Angereichert(name, value).Should().Be(value);
    }

    /// <summary>Exact, never as a substring.</summary>
    /// <remarks>
    /// The rule behind both halves above. <c>Emails</c> is not <c>Email</c>, and
    /// <c>SubjectId</c> is not <c>Id</c> — a comparison that hits substrings
    /// eventually redacts half the log.
    /// </remarks>
    [Theory]
    [InlineData("Emailvorlage")]
    [InlineData("StadtteilCity")]
    [InlineData("Benutzername")]
    public void A_similar_name_is_not_hit(string name)
    {
        Angereichert(name, "sichtbar").Should().Be("sichtbar");
    }

    /// <summary>
    /// <strong>Our own log names stay readable.</strong>
    /// </summary>
    /// <remarks>
    /// The reason the change had no consequences here: this system's templates
    /// are German, and Noelia's list is English. That was not a plan, but it is
    /// a promise now — whoever names a log property <c>Email</c> makes it
    /// unreadable, and this test says so.
    /// </remarks>
    [Theory]
    [InlineData("Art")]
    [InlineData("EntwurfId")]
    [InlineData("Grund")]
    [InlineData("Modul")]
    [InlineData("Dienst")]
    public void Our_own_names_stay_readable(string name)
    {
        Angereichert(name, "lesbar").Should().Be("lesbar");
    }

    /// <summary>The list is one, not three.</summary>
    /// <remarks>
    /// <c>SensitiveFieldNames</c> is the same source the CQRS behaviour and the
    /// HTTP middleware read as well. Three lists would agree on the day they
    /// were written, and never again.
    /// </remarks>
    [Fact]
    public void The_list_carries_the_new_names_and_not_the_old_ones()
    {
        DataMaskingEnricher.IsSensitiveKey("Email").Should().BeTrue();
        DataMaskingEnricher.IsSensitiveKey("SecretName").Should().BeFalse();
        SensitiveFieldNames.All.Should().Contain("Email");
    }
}
