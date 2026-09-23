namespace WorkerTransfer.Jobs.Domain.Jobs;

/// <summary>"Within N kilometres of this point."</summary>
/// <remarks>
/// <strong>The point comes from the browser and is imprecise on purpose.</strong>
/// What arrives here is rounded to two decimal places — a good kilometre. That
/// is no convenience but data minimisation: adverts carry a PLACE NAME, no house
/// number, and their coordinates are those of the town centre. A more precise
/// position of the searching person could change no answer — it would only stand
/// in the access logs.
/// <para>
/// That is also why no home town and no address stands here: nothing about this
/// filter is stored permanently. It lives in a query and ends with it.
/// </para>
/// </remarks>
public sealed record SearchRadius
{
    /// <summary>The smallest sensible radius.</summary>
    /// <remarks>
    /// Ten and not one: the adverts' coordinates are town centres, and a radius
    /// below the extent of a town would claim a precision the data does not
    /// have.
    /// </remarks>
    public const double MinRadiusKm = 10;

    /// <summary>The largest. Beyond it, it is no radius search any more.</summary>
    public const double MaxRadiusKm = 500;

    private SearchRadius(double latitude, double longitude, double radiusKm)
    {
        Latitude = latitude;
        Longitude = longitude;
        RadiusKm = radiusKm;
    }

    /// <summary>Latitude of the centre.</summary>
    public double Latitude { get; }

    /// <summary>Longitude of the centre.</summary>
    public double Longitude { get; }

    /// <summary>The radius in kilometres.</summary>
    public double RadiusKm { get; }

    /// <summary>Builds a radius out of what stood in the query.</summary>
    /// <remarks>
    /// Something incomplete or nonsensical yields <c>null</c> — so NO filter,
    /// and no error. The same attitude as with the PageSelection: a list that
    /// answers <c>?lat=abc</c> with 400 is strict in a place where nobody gains
    /// anything. The radius is clamped instead of discarded — whoever sends
    /// 100000 means "far", and making no filter of it would be the more
    /// surprising answer.
    /// </remarks>
    /// <param name="latitude">Latitude, or <c>null</c>.</param>
    /// <param name="longitude">Longitude, or <c>null</c>.</param>
    /// <param name="radiusKm">The radius wanted, or <c>null</c>.</param>
    /// <returns>The radius, or <c>null</c> when none was meant.</returns>
    public static SearchRadius? From(double? latitude, double? longitude, double? radiusKm)
    {
        if (latitude is not { } lat || longitude is not { } lon || radiusKm is not { } r)
        {
            return null;
        }

        if (double.IsNaN(lat) || double.IsNaN(lon) || double.IsNaN(r)
            || lat is < -90 or > 90 || lon is < -180 or > 180)
        {
            return null;
        }

        return new SearchRadius(lat, lon, Math.Clamp(r, MinRadiusKm, MaxRadiusKm));
    }
}
