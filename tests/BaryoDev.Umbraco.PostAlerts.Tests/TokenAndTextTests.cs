using System.Net;
using BaryoDev.Umbraco.PostAlerts.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Time.Testing;

namespace BaryoDev.Umbraco.PostAlerts.Tests;

public class TokenAndTextTests
{
    [Fact]
    public void Confirmation_tokens_are_random_url_safe_and_long_enough()
    {
        var tokens = new TokenService(new EphemeralDataProtectionProvider());

        var seen = new HashSet<string>();
        for (int i = 0; i < 50; i++)
        {
            string t = tokens.NewConfirmationToken();
            Assert.Equal(43, t.Length);
            Assert.Matches("^[A-Za-z0-9_-]+$", t);
            Assert.True(seen.Add(t));
        }
    }

    [Fact]
    public void The_hash_is_stable_hex_and_differs_per_token()
    {
        string a = TokenService.Hash("abc");

        Assert.Equal(a, TokenService.Hash("abc"));
        Assert.NotEqual(a, TokenService.Hash("abd"));
        Assert.Equal(64, a.Length);
        Assert.Equal("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", a);
    }

    [Fact]
    public void An_unsubscribe_token_round_trips()
    {
        var tokens = new TokenService(new EphemeralDataProtectionProvider());
        Guid id = Guid.NewGuid();

        string token = tokens.UnsubscribeToken(id);

        Assert.Equal(id, tokens.ReadUnsubscribeToken(token));
        Assert.Matches("^[A-Za-z0-9_-]+$", token);
        Assert.True(token.Length < 600);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("%%%")]
    [InlineData("AAAA")]
    public void Garbage_reads_as_no_token(string? value)
    {
        Assert.Null(new TokenService(new EphemeralDataProtectionProvider()).ReadUnsubscribeToken(value));
    }

    [Fact]
    public void Summary_strips_markup_and_entities()
    {
        Assert.Equal("Hello there & welcome", TextCleaner.Summary("<p>Hello <em>there</em> &amp; welcome</p>", 100));
        Assert.Equal("a b", TextCleaner.Summary("a<br>b", 100));
        Assert.Equal("", TextCleaner.Summary(null, 100));
        Assert.Equal("", TextCleaner.Summary("<style>p{}</style><script>x</script>", 100));
    }

    [Fact]
    public void Cut_stops_at_a_word_and_adds_three_dots()
    {
        Assert.Equal("one two...", TextCleaner.Cut("one two three four", 12));
        Assert.Equal("short", TextCleaner.Cut("short", 12));
        Assert.Equal("abcdefghij...", TextCleaner.Cut("abcdefghijklmnop", 10));
    }

    [Fact]
    public void Cut_does_not_split_a_surrogate_pair()
    {
        string text = "aaaa" + "\U0001F600" + "bbbb";

        string cut = TextCleaner.Cut(text, 5);

        Assert.DoesNotContain('\uD83D', cut.Replace("\U0001F600", ""));
    }

    [Theory]
    [InlineData("https://example.com", "/blog/x/", "https://example.com/blog/x/")]
    [InlineData("https://example.com/", "/blog/x/", "https://example.com/blog/x/")]
    [InlineData("https://example.com/site", "/blog/x/", "https://example.com/site/blog/x/")]
    [InlineData("https://example.com", "https://cdn.example.net/a.jpg", "https://cdn.example.net/a.jpg")]
    [InlineData("https://example.com", "//cdn.example.net/a.jpg", "https://cdn.example.net/a.jpg")]
    [InlineData("https://example.com", "/media/a.jpg?width=600&height=315", "https://example.com/media/a.jpg?width=600&height=315")]
    public void Links_are_made_absolute(string site, string url, string expected)
    {
        Assert.Equal(expected, AbsoluteUrl.Make(site, url));
    }

    [Theory]
    [InlineData("https://example.com", "javascript:alert(1)")]
    [InlineData("https://example.com", "relative/path")]
    [InlineData("https://example.com", "")]
    [InlineData("", "/blog/")]
    [InlineData("ftp://example.com", "/blog/")]
    public void Unusable_links_give_null(string site, string url)
    {
        Assert.Null(AbsoluteUrl.Make(site, url));
    }

    [Fact]
    public void Client_keys_keep_ipv4_and_reduce_ipv6_to_the_prefix()
    {
        Assert.Equal("203.0.113.9", ClientKey.From(IPAddress.Parse("203.0.113.9")));
        Assert.Equal("203.0.113.9", ClientKey.From(IPAddress.Parse("::ffff:203.0.113.9")));
        Assert.Equal("2001:db8:1:2::/64", ClientKey.From(IPAddress.Parse("2001:db8:1:2:aaaa:bbbb:cccc:dddd")));
        Assert.Equal(ClientKey.From(IPAddress.Parse("2001:db8:1:2:1::1")), ClientKey.From(IPAddress.Parse("2001:db8:1:2:2::2")));
        Assert.Equal("unknown", ClientKey.From(null));
    }

    [Fact]
    public void The_rate_limiter_blocks_over_the_limit_and_recovers()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.Zero));
        var limiter = new RateLimiter(time);
        var window = TimeSpan.FromMinutes(10);

        Assert.True(limiter.TryAcquire("c", 2, window));
        Assert.True(limiter.TryAcquire("c", 2, window));
        Assert.False(limiter.TryAcquire("c", 2, window));
        Assert.True(limiter.TryAcquire("other", 2, window));
        time.Advance(TimeSpan.FromMinutes(11));
        Assert.True(limiter.TryAcquire("c", 2, window));
    }
}
