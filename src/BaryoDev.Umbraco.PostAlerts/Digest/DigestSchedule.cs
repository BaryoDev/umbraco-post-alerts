namespace BaryoDev.Umbraco.PostAlerts.Digest;

/// <summary>A digest covers posts dated from <see cref="StartUtc"/> up to, but not including, <see cref="EndUtc"/>.</summary>
public readonly record struct DigestPeriod(DateTime StartUtc, DateTime EndUtc);

/// <summary>
/// Pure schedule maths. The send moment is the configured weekday and hour in the configured time zone. A period
/// ends at a send moment and starts at the end of the previous run, so a missed week is folded into the next
/// digest and goes out once.
/// </summary>
public static class DigestSchedule
{
    /// <summary>The latest send moment at or before <paramref name="nowUtc"/>.</summary>
    public static DateTime LatestSendMoment(DateTime nowUtc, DayOfWeek day, int hour, TimeZoneInfo zone)
    {
        DateTime localNow = TimeZoneInfo.ConvertTimeFromUtc(AsUtc(nowUtc), zone);
        int back = ((int)localNow.DayOfWeek - (int)day + 7) % 7;
        DateTime date = localNow.Date.AddDays(-back);
        DateTime moment = SendMoment(date, hour, zone);
        return moment <= AsUtc(nowUtc) ? moment : SendMoment(date.AddDays(-7), hour, zone);
    }

    /// <summary>The first send moment after <paramref name="nowUtc"/>.</summary>
    public static DateTime NextSendMoment(DateTime nowUtc, DayOfWeek day, int hour, TimeZoneInfo zone)
    {
        DateTime latest = LatestSendMoment(nowUtc, day, hour, zone);
        DateTime localDate = TimeZoneInfo.ConvertTimeFromUtc(latest, zone).Date;
        return SendMoment(localDate.AddDays(7), hour, zone);
    }

    /// <summary>The period that is due now, or null when the latest send moment has already been handled.</summary>
    public static DigestPeriod? Due(DateTime nowUtc, DateTime? lastPeriodEndUtc, DayOfWeek day, int hour, TimeZoneInfo zone)
    {
        DateTime end = LatestSendMoment(nowUtc, day, hour, zone);
        if (lastPeriodEndUtc is { } last && AsUtc(last) >= end)
        {
            return null;
        }
        DateTime start;
        if (lastPeriodEndUtc is { } previous)
        {
            start = AsUtc(previous);
        }
        else
        {
            DateTime localDate = TimeZoneInfo.ConvertTimeFromUtc(end, zone).Date;
            start = SendMoment(localDate.AddDays(-7), hour, zone);
        }
        return new DigestPeriod(start, end);
    }

    /// <summary>The UTC instant of <paramref name="hour"/> o'clock on a local date. A time that does not exist moves forward an hour, and a repeated time takes the first one.</summary>
    internal static DateTime SendMoment(DateTime localDate, int hour, TimeZoneInfo zone)
    {
        DateTime local = DateTime.SpecifyKind(localDate.Date.AddHours(hour), DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }
        TimeSpan offset = zone.IsAmbiguousTime(local)
            ? zone.GetAmbiguousTimeOffsets(local).Max()
            : zone.GetUtcOffset(local);
        return DateTime.SpecifyKind(local - offset, DateTimeKind.Utc);
    }

    internal static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
