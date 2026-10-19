using System.Globalization;
using Noelia.Core.Identity;

namespace WorkerTransfer.Advisor.Domain.Mandates;

/// <summary>Something about the input is wrong.</summary>
/// <remarks>
/// The message names the <em>rule</em> and never the value. A salary in an error
/// message would end up in the log — and that is the one place it must never
/// stand here.
/// </remarks>
/// <param name="message">What the rule demands.</param>
public sealed class InputError(string message) : Exception(message);

/// <summary>
/// The mandate — four values, and all four voluntary.
/// </summary>
/// <remarks>
/// <para><strong>The mandate is a VIEW, not a second store</strong>
/// (ADR-0037 decision 1). Who may see me stands in the ledger; whether I am
/// approachable stands in the market status. Holding either here once more
/// would break ADR-0020 word for word — and a revocation would then have to
/// take effect in two places, so one day in one of them it would not.</para>
///
/// <para>What remains are exactly the four values that stand nowhere today:
/// entry month, salary range, workload, excluded companies. No field is called
/// <c>sichtbar</c>, <c>visible</c>, <c>public</c> or <c>freigabe</c>, and a
/// test holds that.</para>
///
/// <para><strong>Empty is the normal case.</strong> A mandate without a single
/// filled value is complete; nobody has to name a salary to hold a
/// conversation. That is why there is no "incomplete" and no mandatory
/// field.</para>
/// </remarks>
public sealed class Mandate
{
    /// <summary>How many companies a person may exclude.</summary>
    /// <remarks>
    /// A limit, so the list does not become a store nobody cleans up — generous
    /// enough that nobody meets it in everyday use.
    /// </remarks>
    public const int MaxExclusions = 50;

    /// <summary>How long a domain may be.</summary>
    public const int MaxDomainLength = 253;

    /// <summary>The smallest workload that is a statement.</summary>
    public const int MinWorkload = 10;

    /// <summary>The largest.</summary>
    public const int MaxWorkload = 100;

    private Mandate(
        SubjectId subject,
        string? entryMonth,
        int? salaryMin,
        int? salaryMax,
        int? workloadPercent,
        IReadOnlyList<string> excluded,
        DateTimeOffset updatedAt)
    {
        Subject = subject;
        EntryMonth = entryMonth;
        SalaryMin = salaryMin;
        SalaryMax = salaryMax;
        WorkloadPercent = workloadPercent;
        ExcludedDomains = excluded;
        UpdatedAt = updatedAt;
    }

    /// <summary>Whose mandate. It <em>is</em> the key.</summary>
    public SubjectId Subject { get; }

    /// <summary>From when, as a month <c>YYYY-MM</c> — or <c>null</c>.</summary>
    /// <remarks>
    /// A month and not a date: "from March" is what a human being with a running
    /// contract can say. A day would pretend a precision that depends on a
    /// notice period nobody has worked out yet.
    /// </remarks>
    public string? EntryMonth { get; }

    /// <summary>Lower bound of the range, euros per month — or <c>null</c>.</summary>
    /// <remarks>
    /// Whole euros and no cents. A range is a <em>statement</em>, not an amount
    /// that is moved; two decimal places on it would read like an invoice.
    /// </remarks>
    public int? SalaryMin { get; }

    /// <summary>Upper bound of the range. See <see cref="SalaryMin"/>.</summary>
    public int? SalaryMax { get; }

    /// <summary>What percentage of a full-time job, 10–100 — or <c>null</c>.</summary>
    public int? WorkloadPercent { get; }

    /// <summary>
    /// Companies no conversation comes about with — as domains.
    /// </summary>
    /// <remarks>
    /// <para><strong>This list never leaves the service.</strong> No company
    /// learns that it is on it, and none learns that a list exists: a
    /// conversation with an excluded company fails with the same 404 as one
    /// with a human being who does not exist.</para>
    ///
    /// <para><strong>And it is the reason "everyone except these" does not
    /// exist.</strong> The ledger knows no denial; "publicly visible, but not
    /// to X" cannot be expressed in it. Whoever names an exclusion therefore
    /// gives up <c>profile.visibility:public</c> — the service writes that, in
    /// the person's name, into the same ledger. Only then does the promise hold
    /// that the current employer does not see their own staff in the
    /// scout.</para>
    /// </remarks>
    public IReadOnlyList<string> ExcludedDomains { get; }

    /// <summary>When something last changed.</summary>
    public DateTimeOffset UpdatedAt { get; }

    /// <summary>Whether anything was said at all.</summary>
    public bool IsEmpty =>
        EntryMonth is null
        && SalaryMin is null
        && SalaryMax is null
        && WorkloadPercent is null
        && ExcludedDomains.Count == 0;

    /// <summary>The empty mandate. "Nothing said" is a state.</summary>
    public static Mandate Empty(SubjectId subject, DateTimeOffset now) =>
        new(subject, null, null, null, null, [], now);

    /// <summary>Writes a mandate after every entry has been checked.</summary>
    /// <remarks>
    /// Check first, then build: a rejected form must not leave a half-written
    /// mandate behind.
    /// </remarks>
    /// <exception cref="InputError">An entry breaks the rule.</exception>
    public static Mandate Write(
        SubjectId subject,
        string? entryMonth,
        int? salaryMin,
        int? salaryMax,
        int? workloadPercent,
        IReadOnlyList<string>? excluded,
        DateTimeOffset now)
    {
        var month = Month(entryMonth);
        var workload = Workload(workloadPercent);
        var salary = SalarySpan(salaryMin, salaryMax);
        var domains = CleanDomains(excluded);

        return new Mandate(subject, month, salary.Min, salary.Max, workload, domains, now);
    }

    /// <summary>The mandate as a row holds it.</summary>
    public static Mandate Restore(
        SubjectId subject,
        string? entryMonth,
        int? salaryMin,
        int? salaryMax,
        int? workloadPercent,
        IReadOnlyList<string> excluded,
        DateTimeOffset updatedAt) =>
        new(subject, entryMonth, salaryMin, salaryMax, workloadPercent, excluded, updatedAt);

    /// <summary>Does this mandate exclude this domain?</summary>
    /// <remarks>
    /// Exact comparison on the whole domain, no suffix match: otherwise
    /// <c>example.com</c> would also exclude <c>notexample.com</c>, and the
    /// human being would never learn why a conversation does not come about.
    /// </remarks>
    public bool Excludes(string? domain) =>
        domain is { Length: > 0 }
        && ExcludedDomains.Contains(
            domain.Trim().ToLowerInvariant(), StringComparer.Ordinal);

    private static string? Month(string? raw)
    {
        var value = (raw ?? string.Empty).Trim();

        if (value.Length == 0)
        {
            return null;
        }

        return DateTime.TryParseExact(
            value, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            ? value
            // web/src/features/work/pages/AdvisorPage.tsx (MANDATE_CASES) matches this sentence.
            : throw new InputError("an entry date is a month, written YYYY-MM");
    }

    // web/src/features/work/pages/AdvisorPage.tsx (MANDATE_CASES) matches this sentence.
    private static int? Workload(int? raw) => raw switch
    {
        null => null,
        < MinWorkload or > MaxWorkload => throw new InputError(
            $"a workload is between {MinWorkload} and {MaxWorkload} percent"),
        _ => raw
    };

    private static (int? Min, int? Max) SalarySpan(int? min, int? max)
    {
        if (min is < 0 || max is < 0)
        {
            // web/src/features/work/pages/AdvisorPage.tsx (MANDATE_CASES) matches this sentence.
            throw new InputError("a salary is not negative");
        }

        if (min is { } lower && max is { } upper && lower > upper)
        {
            // web/src/features/work/pages/AdvisorPage.tsx (MANDATE_CASES) matches this sentence.
            throw new InputError("the lower bound of a salary range is not above the upper");
        }

        return (min, max);
    }

    private static IReadOnlyList<string> CleanDomains(IReadOnlyList<string>? raw)
    {
        if (raw is null or { Count: 0 })
        {
            return [];
        }

        var clean = raw
            .Select(entry => (entry ?? string.Empty).Trim().ToLowerInvariant())
            .Where(entry => entry.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (clean.Length > MaxExclusions)
        {
            // web/src/features/work/pages/AdvisorPage.tsx (MANDATE_CASES) matches this sentence.
            throw new InputError(
                $"at most {MaxExclusions} excluded companies");
        }

        foreach (var entry in clean)
        {
            if (entry.Length > MaxDomainLength
                || !entry.Contains('.', StringComparison.Ordinal)
                || entry.Contains('@', StringComparison.Ordinal)
                || entry.Contains(' ', StringComparison.Ordinal))
            {
                // The rule, never the value: a domain in a message would end
                // up in the log, and it says where somebody works.
                // web/src/features/work/pages/AdvisorPage.tsx (MANDATE_CASES) matches this sentence.
                throw new InputError("an excluded company is named by its domain");
            }
        }

        return clean;
    }
}

/// <summary>Finds and stores mandates.</summary>
public interface IMandateStore
{
    /// <summary>This person's mandate, or <c>null</c>.</summary>
    Task<Mandate?> GetAsync(SubjectId subject, CancellationToken cancellationToken = default);

    /// <summary>Creates or writes back.</summary>
    Task SaveAsync(Mandate mandate, CancellationToken cancellationToken = default);
}
