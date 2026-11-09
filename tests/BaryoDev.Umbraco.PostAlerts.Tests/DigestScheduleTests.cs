using BaryoDev.Umbraco.PostAlerts.Digest;

namespace BaryoDev.Umbraco.PostAlerts.Tests;

public class DigestScheduleTests
{
    private static readonly TimeZoneInfo Manila = TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila");
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    private static DateTime Utc(int y, int m, int d, int h = 0, int min = 0) => Posts.Utc(y, m, d, h, min);

    [Fact]
    public void Latest_moment_is_this_monday_after_the_send_hour()
    {
        // Monday 9 Nov 2026, 10:00 in Manila.
        DateTime latest = DigestSchedule.LatestSendMoment(Utc(2026, 11, 9, 2), DayOfWeek.Monday, 9, Manila);

        Assert.Equal(Utc(2026, 11, 9, 1), latest);
    }

    [Fact]
    public void Latest_moment_is_last_monday_before_the_send_hour()
    {
        // Monday 08:30 in Manila.
        DateTime latest = DigestSchedule.LatestSendMoment(Utc(2026, 11, 9, 0, 30), DayOfWeek.Monday, 9, Manila);

        Assert.Equal(Utc(2026, 11, 2, 1), latest);
    }

    [Fact]
    public void The_time_zone_decides_the_weekday()
    {
        // Sunday 17:00 UTC is already Monday 01:00 in Manila, still before 09:00.
        DateTime latest = DigestSchedule.LatestSendMoment(Utc(2026, 11, 8, 17), DayOfWeek.Monday, 9, Manila);

        Assert.Equal(Utc(2026, 11, 2, 1), latest);
    }

    [Fact]
    public void The_exact_send_moment_counts_as_reached()
    {
        Assert.Equal(Utc(2026, 11, 9, 1), DigestSchedule.LatestSendMoment(Utc(2026, 11, 9, 1), DayOfWeek.Monday, 9, Manila));
        Assert.Equal(Utc(2026, 11, 2, 1), DigestSchedule.LatestSendMoment(Utc(2026, 11, 9, 0, 59), DayOfWeek.Monday, 9, Manila));
    }

    [Theory]
    [InlineData(DayOfWeek.Sunday)]
    [InlineData(DayOfWeek.Wednesday)]
    [InlineData(DayOfWeek.Saturday)]
    public void Other_send_days_land_on_that_weekday(DayOfWeek day)
    {
        DateTime latest = DigestSchedule.LatestSendMoment(Utc(2026, 11, 9, 2), day, 9, Manila);
        DateTime local = TimeZoneInfo.ConvertTimeFromUtc(latest, Manila);

        Assert.Equal(day, local.DayOfWeek);
        Assert.Equal(9, local.Hour);
        Assert.True(latest <= Utc(2026, 11, 9, 2));
        Assert.True(latest > Utc(2026, 11, 9, 2).AddDays(-7));
    }

    [Fact]
    public void First_run_covers_the_week_before_the_latest_moment()
    {
        DigestPeriod? due = DigestSchedule.Due(Utc(2026, 11, 9, 2), null, DayOfWeek.Monday, 9, Manila);

        Assert.NotNull(due);
        Assert.Equal(Utc(2026, 11, 2, 1), due.Value.StartUtc);
        Assert.Equal(Utc(2026, 11, 9, 1), due.Value.EndUtc);
    }

    [Fact]
    public void Nothing_is_due_once_the_latest_moment_has_run()
    {
        Assert.Null(DigestSchedule.Due(Utc(2026, 11, 9, 2), Utc(2026, 11, 9, 1), DayOfWeek.Monday, 9, Manila));
        Assert.Null(DigestSchedule.Due(Utc(2026, 11, 12, 2), Utc(2026, 11, 9, 1), DayOfWeek.Monday, 9, Manila));
    }

    [Fact]
    public void Nothing_is_due_before_the_hour_when_last_week_ran()
    {
        Assert.Null(DigestSchedule.Due(Utc(2026, 11, 9, 0, 30), Utc(2026, 11, 2, 1), DayOfWeek.Monday, 9, Manila));
    }

    [Fact]
    public void The_next_week_is_due_after_the_hour()
    {
        DigestPeriod? due = DigestSchedule.Due(Utc(2026, 11, 9, 1, 5), Utc(2026, 11, 2, 1), DayOfWeek.Monday, 9, Manila);

        Assert.Equal(new DigestPeriod(Utc(2026, 11, 2, 1), Utc(2026, 11, 9, 1)), due);
    }

    [Fact]
    public void A_missed_week_is_caught_up_in_one_period()
    {
        // Last ran 26 Oct. The server was down on 2 Nov and 9 Nov. On Wednesday 11 Nov one period covers both weeks.
        DigestPeriod? due = DigestSchedule.Due(Utc(2026, 11, 11, 5), Utc(2026, 10, 26, 1), DayOfWeek.Monday, 9, Manila);

        Assert.Equal(new DigestPeriod(Utc(2026, 10, 26, 1), Utc(2026, 11, 9, 1)), due);
        // After that run there is nothing left to catch up.
        Assert.Null(DigestSchedule.Due(Utc(2026, 11, 11, 5), Utc(2026, 11, 9, 1), DayOfWeek.Monday, 9, Manila));
    }

    [Fact]
    public void Next_moment_is_a_week_after_the_latest()
    {
        Assert.Equal(Utc(2026, 11, 16, 1), DigestSchedule.NextSendMoment(Utc(2026, 11, 9, 2), DayOfWeek.Monday, 9, Manila));
        Assert.Equal(Utc(2026, 11, 9, 1), DigestSchedule.NextSendMoment(Utc(2026, 11, 9, 0, 30), DayOfWeek.Monday, 9, Manila));
    }

    [Fact]
    public void Daylight_saving_shifts_the_utc_time_but_not_the_local_hour()
    {
        // New York springs forward on 8 Mar 2026. Monday 2 Mar is EST (UTC-5), Monday 9 Mar is EDT (UTC-4).
        DigestPeriod? due = DigestSchedule.Due(Utc(2026, 3, 9, 15), Utc(2026, 3, 2, 14), DayOfWeek.Monday, 9, NewYork);

        Assert.Equal(new DigestPeriod(Utc(2026, 3, 2, 14), Utc(2026, 3, 9, 13)), due);
    }

    [Fact]
    public void First_run_across_the_clock_change_starts_at_the_local_hour_a_week_earlier()
    {
        DigestPeriod? due = DigestSchedule.Due(Utc(2026, 3, 9, 15), null, DayOfWeek.Monday, 9, NewYork);

        Assert.Equal(new DigestPeriod(Utc(2026, 3, 2, 14), Utc(2026, 3, 9, 13)), due);
    }

    [Fact]
    public void A_local_hour_that_does_not_exist_moves_forward()
    {
        // 2:00 on 8 Mar 2026 never happens in New York. 3:00 EDT is 07:00 UTC.
        Assert.Equal(Utc(2026, 3, 8, 7), DigestSchedule.SendMoment(new DateTime(2026, 3, 8), 2, NewYork));
    }

    [Fact]
    public void A_local_hour_that_happens_twice_takes_the_first()
    {
        // 1:00 on 1 Nov 2026 happens as EDT and again as EST. The first is 05:00 UTC.
        Assert.Equal(Utc(2026, 11, 1, 5), DigestSchedule.SendMoment(new DateTime(2026, 11, 1), 1, NewYork));
    }

    [Fact]
    public void Unspecified_and_local_kinds_are_read_as_given()
    {
        var unspecified = new DateTime(2026, 11, 9, 1, 0, 0, DateTimeKind.Unspecified);

        Assert.Equal(DateTimeKind.Utc, DigestSchedule.AsUtc(unspecified).Kind);
        Assert.Equal(unspecified, DigestSchedule.AsUtc(unspecified));
    }
}
