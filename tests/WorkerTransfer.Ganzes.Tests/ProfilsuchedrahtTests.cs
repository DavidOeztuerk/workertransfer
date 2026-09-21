using System.Text.Json;
using FluentAssertions;
using WorkerTransfer.Profile.Contracts;
using WorkerTransfer.Scout.Contracts;

namespace WorkerTransfer.Ganzes.Tests;

/// <summary>
/// What profile-service writes, scout-service has to be able to read.
/// </summary>
/// <remarks>
/// <para><strong>Why this test and not a comparison of names.</strong> A test
/// that asks "is the field called <c>subject_id</c>?" checks the same string
/// twice and breaks together with it. Here the body the sender really writes is
/// serialised instead, and read with the <em>receiver's type</em>. Green then
/// means: the values arrive. That is the promise, and it survives every
/// rename.</para>
///
/// <para><strong>The occasion is older than this service.</strong> Four
/// services once sent <c>userId</c> to a receiver that declares
/// <c>[JsonPropertyName("user_id")]</c>: the id bound silently to
/// <c>Guid.Empty</c>, and the whole notification path never worked — 18 outbox
/// rows, not a single mail. With single-word fields (<c>headline</c>,
/// <c>location</c>) such a thing never shows; only a compound name comes apart,
/// and <c>subject_id</c> and <c>remote_ok</c> are exactly that.</para>
///
/// <para><strong>Why no other suite catches it.</strong>
/// <c>DrahtvertragTests</c> checks that both sides carry <em>an</em> annotation
/// — not that it is the same one. And no service suite crosses the seam: in the
/// tests the sender has no receiver, the receiver no sender.</para>
/// </remarks>
public class ProfilsuchedrahtTests
{
    private static readonly Guid Wer = Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301");

    /// <summary>A profile survives the seam — with id and remote tick.</summary>
    [Fact]
    public void A_profile_arrives_complete()
    {
        var geschrieben = JsonSerializer.Serialize(
            new ProfileHitV1(Wer, "Entwicklerin", "Ich baue verteilte Systeme.", "Berlin", true, ["Go", "PostgreSQL"]));

        var gelesen = JsonSerializer.Deserialize<ForeignProfileV1>(geschrieben);

        gelesen.Should().NotBeNull();
        gelesen!.SubjectId.Should().Be(Wer, "ohne die Kennung ist ein Treffer niemand");
        gelesen.Headline.Should().Be("Entwicklerin");
        gelesen.Bio.Should().Be("Ich baue verteilte Systeme.");
        gelesen.Location.Should().Be("Berlin");
        gelesen.RemoteOk.Should().BeTrue(
            "ein bool hat keine Not-Null-Sperre: er fiele still auf false zurück");
        gelesen.Skills.Should().Equal("Go", "PostgreSQL");
    }

    /// <summary>And a page together with its cursor.</summary>
    /// <remarks>
    /// Without the cursor there would be no second page — and that would look
    /// like "there is nobody else".
    /// </remarks>
    [Fact]
    public void A_page_arrives_with_its_cursor()
    {
        var geschrieben = JsonSerializer.Serialize(
            new ProfileHitPageV1(
                [new ProfileHitV1(Wer, "Entwicklerin", "", "Berlin", false, ["Go"])],
                "ZWVpbg=="));

        var gelesen = JsonSerializer.Deserialize<ForeignProfilePageV1>(geschrieben);

        gelesen.Should().NotBeNull();
        gelesen!.Items.Should().ContainSingle().Which.SubjectId.Should().Be(Wer);
        gelesen.Next.Should().Be("ZWVpbg==");
    }

    /// <summary>
    /// The counter-probe: the test would also notice a wrongly named field.
    /// </summary>
    /// <remarks>
    /// A suite that cannot find anything is green and worthless. Here the same
    /// body is written with the camelCase name a forgotten
    /// <c>[JsonPropertyName]</c> would produce — and the receiver then reads an
    /// empty id.
    /// </remarks>
    [Fact]
    public void A_wrongly_named_field_shows_up()
    {
        var camelCase = """
            {"subjectId":"3f2504e0-4f89-11d3-9a0c-0305e82c3301","headline":"x",
             "bio":"z","location":"y","remoteOk":true,"skills":[]}
            """;

        var gelesen = JsonSerializer.Deserialize<ForeignProfileV1>(camelCase);

        gelesen!.SubjectId.Should().Be(
            Guid.Empty, "genau so band `userId` einmal still auf Guid.Empty");
        gelesen.RemoteOk.Should().BeFalse();
    }
}
