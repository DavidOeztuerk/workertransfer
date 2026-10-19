using System.Text.Json;
using FluentAssertions;
using WorkerTransfer.Applications.Infrastructure.Lookup;
using WorkerTransfer.Contracts.Identity;

namespace WorkerTransfer.Applications.Tests;

/// <summary>
/// The receiver deserialises with the sender's type — never two names for the
/// same field. Measured: the notification wire never arrived four times,
/// because an anonymous object wrote <c>userId</c> and the receiver read
/// <c>user_id</c>.
/// </summary>
public sealed class MemberWireTests
{
    /// <summary>What identity-service writes arrives as an id.</summary>
    [Fact]
    public void The_id_survives_the_round_trip()
    {
        var subject = Guid.CreateVersion7();
        var written = new CompanyMembersResponseV1(
            [new CompanyMemberV1(subject)]);

        var json = JsonSerializer.Serialize(written);
        json.Should().Contain("subject_id");
        // The shared contract's own wire name (src/shared), not ours to rename.
        json.Should().Contain("mitglieder");
        json.Should().NotContain("subjectId");
        json.Should().NotContain("displayName");
        json.Should().NotContain("display_name");

        var read = JsonSerializer.Deserialize<CompanyMembersResponseV1>(json);

        read.Should().NotBeNull();
        read!.Members.Should().ContainSingle()
            .Which.SubjectId.Should().Be(subject);
    }

    /// <summary>The AI access survives the round trip — an empty key included.</summary>
    [Fact]
    public void The_ai_access_survives_the_round_trip()
    {
        var written = new AiAccessV1(
            "openai_compatible",
            "http://localhost:11434/v1/chat/completions",
            "qwen2.5-coder:7b",
            "");

        var json = JsonSerializer.Serialize(written);
        json.Should().Contain("base_url");
        json.Should().Contain("provider");
        json.Should().NotContain("baseUrl");

        var read = JsonSerializer.Deserialize<AiAccessV1>(json);

        read.Should().NotBeNull();
        read!.Provider.Should().Be("openai_compatible");
        read.Model.Should().Be("qwen2.5-coder:7b");
        read.Key.Should().BeEmpty();
    }

    [Fact]
    public void The_company_name_arrives_as_display_name()
    {
        const string json =
            """{"display_name":"Example Ltd","line1":"Example Street 1","postal_code":"10115","city":"Berlin","country":"DE"}""";

        var read = JsonSerializer.Deserialize<CompanyProfileResponse>(json);

        read.Should().NotBeNull();
        read!.Name.Should().Be("Example Ltd");
        read.Line1.Should().Be("Example Street 1");
        read.City.Should().Be("Berlin");
    }
}
