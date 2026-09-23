using FluentAssertions;
using Noelia.Core.Identity;
using WorkerTransfer.Profile.Domain.Profile;

namespace WorkerTransfer.Profile.Tests;

/// <summary>The cursor carries both sort keys — and forgives nonsense.</summary>
public class PageCursorTests
{
    [Fact]
    public void There_and_back_gives_the_same()
    {
        var cursor = new PageCursor(
            new DateTimeOffset(2026, 8, 22, 9, 0, 0, 123, TimeSpan.Zero), SubjectId.New());

        PageCursor.Read(cursor.Write()).Should().Be(cursor);
    }

    /// <summary>
    /// Two profiles in the same second would skip each other while paging if
    /// the cursor carried only the timestamp.
    /// </summary>
    [Fact]
    public void Two_cursors_at_the_same_time_differ_by_the_person()
    {
        var stamp = DateTimeOffset.UtcNow;

        new PageCursor(stamp, SubjectId.New()).Write()
            .Should().NotBe(new PageCursor(stamp, SubjectId.New()).Write());
    }

    /// <summary>
    /// It comes from a URL and is copied, shortened and passed on. A 400 on it
    /// turns a shared link into an error message.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not-base64!")]
    [InlineData("c29tZXRoaW5n")]
    public void The_unreadable_is_the_beginning_and_no_error(string? nonsense) =>
        PageCursor.Read(nonsense).Should().BeNull();
}
