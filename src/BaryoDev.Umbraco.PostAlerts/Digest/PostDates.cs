namespace BaryoDev.Umbraco.PostAlerts.Digest;

public static class PostDates
{
    /// <summary>
    /// Converts a stored date to UTC. A UTC or local value converts as is. A value with no kind is read in
    /// <paramref name="unspecifiedZone"/>.
    /// </summary>
    public static DateTime ToUtc(DateTime value, TimeZoneInfo unspecifiedZone) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(value, DateTimeKind.Unspecified), unspecifiedZone.IsInvalidTime(value) ? TimeZoneInfo.Utc : unspecifiedZone),
    };

    /// <summary>Reads a date from the value types a date property editor can give.</summary>
    public static DateTime? FromPropertyValue(object? value, TimeZoneInfo zone) => value switch
    {
        DateTime d when d != default => ToUtc(d, zone),
        DateTimeOffset o when o != default => o.UtcDateTime,
        DateOnly d => ToUtc(d.ToDateTime(TimeOnly.MinValue), zone),
        _ => null,
    };
}
