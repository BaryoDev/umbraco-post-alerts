using BaryoDev.Umbraco.PostAlerts.Digest;

namespace BaryoDev.Umbraco.PostAlerts.Tests;

public class DigestSelectionTests
{
    private static readonly DigestPeriod Week = new(Posts.Utc(2026, 11, 2, 1), Posts.Utc(2026, 11, 9, 1));

    [Fact]
    public void Only_posts_inside_the_window_are_selected()
    {
        var posts = new[]
        {
            Posts.Make("Before", Posts.Utc(2026, 11, 2, 0, 59)),
            Posts.Make("At start", Posts.Utc(2026, 11, 2, 1)),
            Posts.Make("Middle", Posts.Utc(2026, 11, 5)),
            Posts.Make("At end", Posts.Utc(2026, 11, 9, 1)),
            Posts.Make("After", Posts.Utc(2026, 11, 10)),
        };

        var selected = DigestSelection.Select(posts, Week, 10);

        Assert.Equal(["Middle", "At start"], selected.Select(p => p.Title));
    }

    [Fact]
    public void A_week_with_no_new_posts_selects_nothing()
    {
        var posts = new[] { Posts.Make("Old", Posts.Utc(2026, 10, 1)) };

        Assert.Empty(DigestSelection.Select(posts, Week, 10));
    }

    [Fact]
    public void Newest_first_with_a_stable_tie_break_and_a_cap()
    {
        var same = Posts.Utc(2026, 11, 4);
        var posts = new[]
        {
            Posts.Make("b post", same),
            Posts.Make("a post", same),
            Posts.Make("newest", Posts.Utc(2026, 11, 8)),
            Posts.Make("oldest", Posts.Utc(2026, 11, 3)),
        };

        Assert.Equal(["newest", "a post", "b post"], DigestSelection.Select(posts, Week, 3).Select(p => p.Title));
    }

    [Fact]
    public void Posts_without_an_address_are_left_out()
    {
        var withUrl = Posts.Make("Linked", Posts.Utc(2026, 11, 4));
        var withoutUrl = withUrl with { Key = Guid.NewGuid(), Title = "Unlinked", Url = null };

        Assert.Equal(["Linked"], DigestSelection.Select([withUrl, withoutUrl], Week, 10).Select(p => p.Title));
    }

    [Fact]
    public void Latest_ignores_the_window()
    {
        var posts = new[] { Posts.Make("One", Posts.Utc(2026, 1, 1)), Posts.Make("Two", Posts.Utc(2026, 2, 1)), Posts.Make("Three", Posts.Utc(2026, 3, 1)) };

        Assert.Equal(["Three", "Two"], DigestSelection.Latest(posts, 2).Select(p => p.Title));
    }

    [Fact]
    public void Date_conversion_reads_unspecified_dates_in_the_site_zone()
    {
        var manila = TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila");

        Assert.Equal(Posts.Utc(2026, 11, 8, 16), PostDates.ToUtc(new DateTime(2026, 11, 9, 0, 0, 0, DateTimeKind.Unspecified), manila));
        Assert.Equal(Posts.Utc(2026, 11, 9), PostDates.ToUtc(Posts.Utc(2026, 11, 9), manila));
    }

    [Fact]
    public void Property_values_of_other_types_give_no_date()
    {
        var manila = TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila");

        Assert.Null(PostDates.FromPropertyValue(null, manila));
        Assert.Null(PostDates.FromPropertyValue("2026-11-09", manila));
        Assert.Null(PostDates.FromPropertyValue(default(DateTime), manila));
        Assert.Equal(Posts.Utc(2026, 11, 9, 3), PostDates.FromPropertyValue(new DateTimeOffset(2026, 11, 9, 11, 0, 0, TimeSpan.FromHours(8)), manila));
        Assert.Equal(Posts.Utc(2026, 11, 8, 16), PostDates.FromPropertyValue(new DateOnly(2026, 11, 9), manila));
    }
}
