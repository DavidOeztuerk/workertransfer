namespace WorkerTransfer.Jobs.Domain.Jobs;

/// <summary>Per what the amounts are meant.</summary>
public enum SalaryPeriod
{
    /// <summary>Gross per year.</summary>
    Year,

    /// <summary>Gross per month.</summary>
    Month,

    /// <summary>Gross per hour.</summary>
    Hour
}

/// <summary>The salary range a company states in its advert (VISION-FRAGEN B5).</summary>
/// <remarks>
/// <para><strong>A number about the JOB, never about a person.</strong> That is
/// ADR-0022's line: requirement in, evidence out. What the job pays is the
/// company's statement; what a person is worth is nobody's.</para>
///
/// <para><strong>Optional, and an advert without one says so.</strong> The EU
/// Pay Transparency Directive (2023/970, Art. 5) asks for the starting pay or
/// its range BEFORE the interview — in the advert or otherwise. Whether
/// WorkerTransfer makes it mandatory is lawyer question J13; until then a
/// missing range is shown as "not stated", never left out silently.</para>
///
/// <para><strong>Whole euros, gross, and both ends.</strong> One end alone
/// ("from 40,000") reads like a range and is a floor with the ceiling hidden.
/// A single figure is a range whose ends coincide.</para>
/// </remarks>
public sealed record SalaryRange
{
    /// <summary>The largest amount accepted — anything above is a typo.</summary>
    public const int Ceiling = 10_000_000;

    private SalaryRange(int minimum, int maximum, SalaryPeriod period)
    {
        Minimum = minimum;
        Maximum = maximum;
        Period = period;
    }

    /// <summary>The lower end, gross, in whole euros.</summary>
    public int Minimum { get; }

    /// <summary>The upper end, gross, in whole euros.</summary>
    public int Maximum { get; }

    /// <summary>Per year, month or hour.</summary>
    public SalaryPeriod Period { get; }

    /// <summary>The range from the form — or <c>null</c> when none was stated.</summary>
    /// <exception cref="SalaryError">Only one end, an end out of bounds, or upside down.</exception>
    public static SalaryRange? From(int? minimum, int? maximum, SalaryPeriod period)
    {
        if (minimum is null && maximum is null)
        {
            return null;
        }

        // web/src/features/company/pages/CompanyJobNewPage.tsx (CREATE_CASES) matches "salary".
        if (minimum is not { } low || maximum is not { } high)
        {
            throw new SalaryError("The salary range needs both ends.");
        }

        if (low <= 0 || high > Ceiling)
        {
            throw new SalaryError($"The salary range must lie between 1 and {Ceiling}.");
        }

        return low > high
            ? throw new SalaryError("The salary range's lower end is above its upper end.")
            : new SalaryRange(low, high, period);
    }

    /// <summary>Back from the store, without checking again.</summary>
    public static SalaryRange Restore(int minimum, int maximum, SalaryPeriod period) =>
        new(minimum, maximum, period);
}
