using FluentAssertions;
using Girder.Core.Logging;
using Girder.Infrastructure.Logging;
using Serilog.Events;
using Serilog.Parsing;

namespace WorkerTransfer.Ganzes.Tests;

/// <summary>Was Girders Maskierung in 4.4.0 schwärzt — und was sie wieder freigibt.</summary>
/// <remarks>
/// <para><strong>Diese Reihe ist die Gegenprobe zu einer Nullmessung.</strong>
/// Am laufenden Stapel steht nirgends <c>[REDACTED]</c>, und das hat zwei
/// mögliche Bedeutungen: es gibt nichts zu maskieren, oder die Maskierung läuft
/// nicht. Ohne diese Tests wäre die erste Lesart eine Hoffnung.</para>
///
/// <para><strong>4.4.0 dreht die Richtung an zwei Stellen</strong>, und beide
/// sind hier festgehalten: verglichen wird jetzt <em>exakt</em> statt als
/// Teilzeichenkette, wodurch <c>SecretName</c> und <c>TokenId</c> wieder lesbar
/// sind — der Name eines Geheimnisses ist keines, und ohne ihn kann ein
/// Betreiber nicht sagen, welches gemeint war. Dafür fallen jetzt
/// <c>Username</c>, <c>Email</c> und <c>City</c> darunter, die es vorher nicht
/// taten.</para>
///
/// <para>Für WorkerTransfer ist beides folgenlos, und auch das ist gemessen:
/// unsere Log-Vorlagen tragen deutsche Namen, und Girders
/// <c>LoggingBehavior</c> schreibt ohnehin <em>Gestalten</em> —
/// <c>Email: string(34)</c> ist eine Länge, nie eine Adresse. Es gibt hier
/// nichts, das dunkel werden könnte.</para>
/// </remarks>
public sealed class MaskierungTests
{
    /// <summary>Ein Ereignis mit genau einer Eigenschaft, durch den Anreicherer.</summary>
    private static string Angereichert(string name, string wert)
    {
        var ereignis = new LogEvent(
            DateTimeOffset.UnixEpoch,
            LogEventLevel.Information,
            exception: null,
            new MessageTemplate("egal", []),
            [new LogEventProperty(name, new ScalarValue(wert))]);

        new DataMaskingEnricher().Enrich(ereignis, propertyFactory: null!);

        return ereignis.Properties[name] is ScalarValue { Value: string gelesen }
            ? gelesen
            : ereignis.Properties[name].ToString().Trim('"');
    }

    /// <summary>Was ab 4.4.0 geschwärzt wird — und vorher sichtbar war.</summary>
    [Theory]
    [InlineData("Username")]
    [InlineData("Email")]
    [InlineData("City")]
    public void Neu_geschwaerzt(string name)
    {
        Angereichert(name, "etwas Persönliches").Should().Be(DataMaskingEnricher.Mask);
    }

    /// <summary>
    /// <strong>Und was 4.4.0 wieder freigibt.</strong>
    /// </summary>
    /// <remarks>
    /// In 4.3.0 traf die Teilzeichenkette „secret" auch <c>SecretName</c> und
    /// „token" auch <c>TokenId</c>. Das war der Fehler, den 4.4.0 behebt: ein
    /// Log, in dem beide geschwärzt sind, kann niemand mehr verfolgen — man
    /// weiß dann, dass ein Geheimnis benutzt wurde, aber nicht welches.
    /// </remarks>
    [Theory]
    [InlineData("SecretName", "jwt-signing-key")]
    [InlineData("TokenId", "01a070e2-847e-78f7")]
    public void Wieder_sichtbar(string name, string wert)
    {
        Angereichert(name, wert).Should().Be(wert);
    }

    /// <summary>Exakt, nie als Teilzeichenkette.</summary>
    /// <remarks>
    /// Die Regel hinter beiden Hälften oben. <c>Emails</c> ist nicht
    /// <c>Email</c>, und <c>SubjectId</c> ist nicht <c>Id</c> — ein Vergleich,
    /// der Teilzeichenketten trifft, schwärzt irgendwann den halben Log.
    /// </remarks>
    [Theory]
    [InlineData("Emailvorlage")]
    [InlineData("StadtteilCity")]
    [InlineData("Benutzername")]
    public void Ein_aehnlicher_Name_wird_nicht_getroffen(string name)
    {
        Angereichert(name, "sichtbar").Should().Be("sichtbar");
    }

    /// <summary>
    /// <strong>Unsere eigenen Log-Namen bleiben lesbar.</strong>
    /// </summary>
    /// <remarks>
    /// Der Grund, warum die Umstellung hier folgenlos blieb: die Vorlagen
    /// dieses Systems sind deutsch, und Girders Liste ist englisch. Das war
    /// kein Plan, aber es ist jetzt eine Zusage — wer eine Log-Eigenschaft
    /// <c>Email</c> nennt, macht sie unlesbar, und dieser Test sagt ihm das.
    /// </remarks>
    [Theory]
    [InlineData("Art")]
    [InlineData("EntwurfId")]
    [InlineData("Grund")]
    [InlineData("Modul")]
    [InlineData("Dienst")]
    public void Unsere_Namen_bleiben_stehen(string name)
    {
        Angereichert(name, "lesbar").Should().Be("lesbar");
    }

    /// <summary>Die Liste ist eine, nicht drei.</summary>
    /// <remarks>
    /// <c>SensitiveFieldNames</c> ist dieselbe Quelle, die auch das
    /// CQRS-Verhalten und die HTTP-Zwischenschicht lesen. Drei Listen wären an
    /// dem Tag einig, an dem sie geschrieben wurden, und nie wieder.
    /// </remarks>
    [Fact]
    public void Die_Liste_traegt_die_neuen_Namen_und_nicht_die_alten()
    {
        DataMaskingEnricher.IsSensitiveKey("Email").Should().BeTrue();
        DataMaskingEnricher.IsSensitiveKey("SecretName").Should().BeFalse();
        SensitiveFieldNames.All.Should().Contain("Email");
    }
}
