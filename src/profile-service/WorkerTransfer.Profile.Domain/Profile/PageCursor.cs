using System.Buffers.Text;
using System.Globalization;
using System.Text;
using Noelia.Core.Identity;

namespace WorkerTransfer.Profile.Domain.Profile;

/// <summary>Where a page continues.</summary>
/// <param name="UpdatedAt">The timestamp of the last row shown.</param>
/// <param name="Subject">Its person.</param>
/// <remarks>
/// Both sort keys, and that is the whole point: <c>UpdatedAt</c> alone is not
/// enough, because two profiles in the same second would skip each other or
/// appear twice while paging.
/// <para>
/// A cursor and no page offset (<c>OFFSET</c>): the offset shifts as soon as
/// somebody changes their profile between two page requests — and here the
/// timestamp changes the order, so exactly that happens all the time.
/// </para>
/// </remarks>
public sealed record PageCursor(DateTimeOffset UpdatedAt, SubjectId Subject)
{
    /// <summary>The text form, as it stands in the URL.</summary>
    /// <remarks>
    /// Base64 not as encryption — it is none — but as a sign to the caller that
    /// the form belongs to us and may change.
    /// </remarks>
    public string Write() =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(
            $"{UpdatedAt.ToString("O", CultureInfo.InvariantCulture)}|{Subject.Value}"));

    /// <summary>Reads the text form back — or gives <c>null</c>.</summary>
    /// <remarks>
    /// An unreadable cursor is no error but the beginning. It comes from a URL
    /// and is copied, shortened and passed on; answering it with a 400 helps
    /// nobody and turns a shared link into an error message.
    /// </remarks>
    /// <param name="text">What stood in the request.</param>
    public static PageCursor? Read(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        Span<byte> buffer = new byte[text.Length];

        if (!Convert.TryFromBase64String(text, buffer, out var written))
        {
            return null;
        }

        var raw = Encoding.UTF8.GetString(buffer[..written]);
        var separator = raw.IndexOf('|', StringComparison.Ordinal);

        if (separator < 0)
        {
            return null;
        }

        return DateTimeOffset.TryParse(
                   raw[..separator], CultureInfo.InvariantCulture,
                   DateTimeStyles.RoundtripKind, out var stamp)
               && Guid.TryParse(raw[(separator + 1)..], out var subject)
            ? new PageCursor(stamp, new SubjectId(subject))
            : null;
    }
}
