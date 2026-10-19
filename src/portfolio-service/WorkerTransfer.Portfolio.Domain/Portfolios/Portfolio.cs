using Noelia.Core.Identity;

namespace WorkerTransfer.Portfolio.Domain.Portfolios;

/// <summary>What somebody has done, in their own order.</summary>
/// <remarks>
/// One portfolio per person; the <c>SubjectId</c> <em>is</em> the key. Who may
/// see it is decided by the consent ledger and not by this module
/// (ADR-0020 §6) — which is why there is not a single visibility field here.
/// </remarks>
public sealed class Portfolio
{
    /// <summary>How many entries a portfolio carries.</summary>
    /// <remarks>
    /// A limit, not an evaluation: thirty entries are a shop window, three
    /// hundred would be a filing cabinet, and nobody reads one.
    /// </remarks>
    public const int MaxEntries = 30;

    private Portfolio(
        SubjectId subject,
        IReadOnlyList<Entry> entries,
        DateTimeOffset createdAt,
        DateTimeOffset changedAt)
    {
        Subject = subject;
        Entries = entries;
        CreatedAt = createdAt;
        ChangedAt = changedAt;
    }

    /// <summary>Whose portfolio this is.</summary>
    public SubjectId Subject { get; }

    /// <summary>
    /// The entries, in the order they came in.
    /// </summary>
    /// <remarks>
    /// A portfolio has no natural order. "This one first" is a decision by the
    /// person, and re-sorting them would mean overriding it.
    /// </remarks>
    public IReadOnlyList<Entry> Entries { get; private set; }

    /// <summary>When it was first written.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>When it last changed.</summary>
    public DateTimeOffset ChangedAt { get; private set; }

    /// <exception cref="TooManyEntries">There are too many.</exception>
    public static Portfolio Create(
        SubjectId subject, IReadOnlyList<Entry> entries, DateTimeOffset now) =>
        new(subject, Checked(entries), now, now);

    /// <summary>The portfolio as a row holds it.</summary>
    public static Portfolio Restore(
        SubjectId subject,
        IReadOnlyList<Entry> entries,
        DateTimeOffset createdAt,
        DateTimeOffset changedAt) =>
        new(subject, entries, createdAt, changedAt);

    /// <summary>Replaces the entries.</summary>
    /// <remarks>
    /// Check everything first, then write: a refused form must not leave a
    /// half-changed aggregate behind.
    /// </remarks>
    /// <exception cref="TooManyEntries">There are too many.</exception>
    public void Change(IReadOnlyList<Entry> entries, DateTimeOffset now)
    {
        Entries = Checked(entries);
        ChangedAt = now;
    }

    /// <summary>Whether this attachment belongs to this portfolio.</summary>
    /// <remarks>
    /// The path to the file is formed from <c>SubjectId</c> and name — but the
    /// question is asked all the same whether an entry names it. Otherwise a
    /// deleted file would stay reachable through its name although it has
    /// disappeared from the portfolio.
    /// </remarks>
    public bool Names(string attachment) =>
        Entries.Any(entry => string.Equals(entry.Attachment, attachment, StringComparison.Ordinal));

    private static IReadOnlyList<Entry> Checked(IReadOnlyList<Entry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        return entries.Count > MaxEntries
            ? throw new TooManyEntries(MaxEntries)
            : entries;
    }
}

/// <summary>Finds and stores portfolios.</summary>
public interface IPortfolioStore
{
    /// <summary>A person's portfolio, or <c>null</c>.</summary>
    Task<Portfolio?> GetAsync(SubjectId subject, CancellationToken cancellationToken = default);

    /// <summary>Creates or writes back.</summary>
    Task SaveAsync(Portfolio portfolio, CancellationToken cancellationToken = default);

    /// <summary>Erases everything about this person.</summary>
    /// <returns>How many rows deliberately stayed. Always zero.</returns>
    Task<int> DeleteAsync(SubjectId subject, CancellationToken cancellationToken = default);
}
