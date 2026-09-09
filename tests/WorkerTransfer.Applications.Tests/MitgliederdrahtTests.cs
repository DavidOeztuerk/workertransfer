using System.Text.Json;
using FluentAssertions;
using WorkerTransfer.Contracts.Identity;

namespace WorkerTransfer.Applications.Tests;

/// <summary>
/// Der Empfänger deserialisiert mit dem Typ des Absenders — nie zwei Namen
/// für dasselbe Feld. Gemessen: der Benachrichtigungsdraht kam viermal nie
/// an, weil ein anonymes Objekt <c>userId</c> schrieb und der Empfänger
/// <c>user_id</c> las.
/// </summary>
public sealed class MitgliederdrahtTests
{
    /// <summary>Was der Identity-Dienst schreibt, kommt als Kennung an.</summary>
    [Fact]
    public void Die_Kennung_ueberlebt_die_Runde()
    {
        var wer = Guid.CreateVersion7();
        var geschrieben = new UnternehmensmitgliederAntwortV1(
            [new UnternehmensmitgliedV1(wer)]);

        var json = JsonSerializer.Serialize(geschrieben);
        json.Should().Contain("subject_id");
        json.Should().Contain("mitglieder");
        json.Should().NotContain("subjectId");
        json.Should().NotContain("displayName");
        json.Should().NotContain("display_name");

        var gelesen = JsonSerializer.Deserialize<UnternehmensmitgliederAntwortV1>(json);

        gelesen.Should().NotBeNull();
        gelesen!.Mitglieder.Should().ContainSingle()
            .Which.SubjectId.Should().Be(wer);
    }

    /// <summary>Der KI-Zugang überlebt die Runde — inklusive leerem Schlüssel.</summary>
    [Fact]
    public void Der_Ki_Zugang_ueberlebt_die_Runde()
    {
        var geschrieben = new KiZugangV1(
            "openai_compatible",
            "http://localhost:11434/v1/chat/completions",
            "qwen2.5-coder:7b",
            "");

        var json = JsonSerializer.Serialize(geschrieben);
        json.Should().Contain("base_url");
        json.Should().Contain("provider");
        json.Should().NotContain("baseUrl");

        var gelesen = JsonSerializer.Deserialize<KiZugangV1>(json);

        gelesen.Should().NotBeNull();
        gelesen!.Provider.Should().Be("openai_compatible");
        gelesen.Model.Should().Be("qwen2.5-coder:7b");
        gelesen.Key.Should().BeEmpty();
    }

    [Fact]
    public void Der_Firmenname_kommt_als_display_name_an()
    {
        const string json =
            """{"display_name":"Muster GmbH","line1":"Weg 1","postal_code":"10115","city":"Berlin","country":"DE"}""";

        var gelesen = JsonSerializer.Deserialize<WorkerTransfer.Applications.Infrastructure.Auskunft.Firmenprofilantwort>(json);

        gelesen.Should().NotBeNull();
        gelesen!.Name.Should().Be("Muster GmbH");
        gelesen.Line1.Should().Be("Weg 1");
        gelesen.City.Should().Be("Berlin");
    }
}
