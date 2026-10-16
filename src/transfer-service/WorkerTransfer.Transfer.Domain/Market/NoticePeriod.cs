using Noelia.Core.Identity;

namespace WorkerTransfer.Transfer.Domain.Market;

/// <summary>In what the notice period is counted.</summary>
public enum NoticeUnit
{
    /// <summary>Weeks.</summary>
    Week,

    /// <summary>Months.</summary>
    Month
}

/// <summary>To which day a notice takes effect.</summary>
/// <remarks>
/// The four forms German contracts and § 622 BGB actually use. Whoever's
/// contract says something else picks the nearest and reads the hint that the
/// computation is an orientation, not a legal statement (§ 2 RDG).
/// </remarks>
public enum NoticeEnd
{
    /// <summary>On any day.</summary>
    AnyDay,

    /// <summary>To the 15th or to the end of a month — § 622 (1) BGB.</summary>
    FifteenthOrMonthEnd,

    /// <summary>To the end of a month.</summary>
    MonthEnd,

    /// <summary>To the end of a calendar quarter.</summary>
    QuarterEnd
}

/// <summary>The notice period does not keep its rule.</summary>
public sealed class NoticeError(string reason) : MarketError(reason);

/// <summary>
/// The person's notice period — and from it the earliest day they could start
/// elsewhere (VISION-FRAGEN H2).
/// </summary>
/// <remarks>
/// <para><strong>Seen by the person only.</strong> Not by a company, not with a
/// market release: the earliest change says when somebody could be gone, and
/// that is the strongest lever in a negotiation. The one exception is a
/// release negotiation over the route she opened to her employer: there both
/// companies see her earliest regular start, under that consent and nowhere
/// else (ADR-0050).</para>
///
/// <para><strong>A date, never a price.</strong> The longer the remaining
/// period, the more a buyout would be worth — and the platform names no
/// figure for it (ADR-0022).</para>
///
/// <para><strong>An orientation, not a legal statement.</strong> Holidays,
/// probation periods, collective agreements and the date the notice is
/// received all shift the real day; the page says so, and the computation
/// never pretends otherwise (§ 2 RDG).</para>
/// </remarks>
public sealed class NoticePeriod
{
    /// <summary>The longest period in months — anything above is a typo.</summary>
    public const int MaxMonths = 24;

    /// <summary>The longest period in weeks.</summary>
    public const int MaxWeeks = 104;

    private NoticePeriod(SubjectId subject, int length, NoticeUnit unit, NoticeEnd end, DateTimeOffset changedAt)
    {
        Subject = subject;
        Length = length;
        Unit = unit;
        End = end;
        ChangedAt = changedAt;
    }

    /// <summary>Whose.</summary>
    public SubjectId Subject { get; }

    /// <summary>How many weeks or months.</summary>
    public int Length { get; }

    /// <summary>Weeks or months.</summary>
    public NoticeUnit Unit { get; }

    /// <summary>To which day it takes effect.</summary>
    public NoticeEnd End { get; }

    /// <summary>When it was stated.</summary>
    public DateTimeOffset ChangedAt { get; }

    /// <summary>A stated notice period.</summary>
    /// <exception cref="NoticeError">Not between 1 and the maximum.</exception>
    public static NoticePeriod State(SubjectId subject, int length, NoticeUnit unit, NoticeEnd end, DateTimeOffset now)
    {
        var limit = unit == NoticeUnit.Month ? MaxMonths : MaxWeeks;

        // web/src/features/work/pages/MarketPage.tsx (NOTICE_CASES) matches "notice period".
        return length < 1 || length > limit
            ? throw new NoticeError($"The notice period must lie between 1 and {limit}.")
            : new NoticePeriod(subject, length, unit, end, now);
    }

    /// <summary>Back from the store, without checking again.</summary>
    public static NoticePeriod Restore(SubjectId subject, int length, NoticeUnit unit, NoticeEnd end, DateTimeOffset changedAt) =>
        new(subject, length, unit, end, changedAt);

    /// <summary>The last day of employment if notice were given on <paramref name="givenOn"/>.</summary>
    public DateOnly LastDay(DateOnly givenOn)
    {
        var plain = Unit == NoticeUnit.Week ? givenOn.AddDays(7 * Length) : givenOn.AddMonths(Length);

        return End switch
        {
            NoticeEnd.AnyDay => plain,
            NoticeEnd.MonthEnd => MonthEnd(plain),
            NoticeEnd.FifteenthOrMonthEnd => plain.Day <= 15
                ? new DateOnly(plain.Year, plain.Month, 15)
                : MonthEnd(plain),
            NoticeEnd.QuarterEnd => QuarterEnd(plain),
            _ => throw new ArgumentOutOfRangeException(nameof(End))
        };
    }

    /// <summary>The earliest day a new job could start — the day after the last one.</summary>
    public DateOnly EarliestStart(DateOnly givenOn) => LastDay(givenOn).AddDays(1);

    private static DateOnly MonthEnd(DateOnly day) =>
        new(day.Year, day.Month, DateTime.DaysInMonth(day.Year, day.Month));

    private static DateOnly QuarterEnd(DateOnly day)
    {
        var lastMonth = ((day.Month - 1) / 3 + 1) * 3;
        return new DateOnly(day.Year, lastMonth, DateTime.DaysInMonth(day.Year, lastMonth));
    }
}

/// <summary>Where notice periods live — one per person.</summary>
public interface INoticeStore
{
    /// <summary>The person's, or <c>null</c>: not stated.</summary>
    Task<NoticePeriod?> GetAsync(SubjectId subject, CancellationToken cancellationToken = default);

    /// <summary>Creates or replaces.</summary>
    Task SaveAsync(NoticePeriod notice, CancellationToken cancellationToken = default);

    /// <summary>Removes it — "I no longer say".</summary>
    Task DeleteAsync(SubjectId subject, CancellationToken cancellationToken = default);
}

/// <summary>The words the wire and the table use.</summary>
public static class NoticeWords
{
    /// <summary>The word for a unit.</summary>
    public static string Word(NoticeUnit unit) => unit == NoticeUnit.Week ? "week" : "month";

    /// <summary>The unit for a word, or <c>null</c>.</summary>
    public static NoticeUnit? ReadUnit(string? word) => word switch
    {
        "week" => NoticeUnit.Week,
        "month" => NoticeUnit.Month,
        _ => null
    };

    /// <summary>The word for an end.</summary>
    public static string Word(NoticeEnd end) => end switch
    {
        NoticeEnd.AnyDay => "any_day",
        NoticeEnd.FifteenthOrMonthEnd => "fifteenth_or_month_end",
        NoticeEnd.MonthEnd => "month_end",
        NoticeEnd.QuarterEnd => "quarter_end",
        _ => throw new ArgumentOutOfRangeException(nameof(end))
    };

    /// <summary>The end for a word, or <c>null</c>.</summary>
    public static NoticeEnd? ReadEnd(string? word) => word switch
    {
        "any_day" => NoticeEnd.AnyDay,
        "fifteenth_or_month_end" => NoticeEnd.FifteenthOrMonthEnd,
        "month_end" => NoticeEnd.MonthEnd,
        "quarter_end" => NoticeEnd.QuarterEnd,
        _ => null
    };

    /// <summary>
    /// The offer's start as a day — <c>yyyy-MM-dd</c>, or <c>yyyy-MM</c> as its
    /// first day — or <c>null</c> when the free text says something else.
    /// </summary>
    /// <remarks>
    /// Unreadable is <c>null</c>, never "fine": the mark then simply does not
    /// stand, and the person reads the start as the company wrote it.
    /// </remarks>
    public static DateOnly? ReadStart(string? start)
    {
        var text = (start ?? string.Empty).Trim();

        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var day))
        {
            return day;
        }

        return DateOnly.TryParseExact(text, "yyyy-MM", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var month)
            ? month
            : null;
    }
}
