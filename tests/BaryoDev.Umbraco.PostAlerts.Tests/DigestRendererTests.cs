using BaryoDev.Umbraco.PostAlerts.Digest;

namespace BaryoDev.Umbraco.PostAlerts.Tests;

public class DigestRendererTests
{
    private static readonly TimeZoneInfo Manila = TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila");
    private const string Unsub = "https://example.com/baryo-post-alerts/unsubscribe/TOKEN123";

    private static DigestEmailModel Model(IReadOnlyList<PostCandidate> posts, bool test = false) =>
        new("Example", "https://example.com", posts, Unsub, Manila, 220, test);

    [Fact]
    public void Each_post_shows_cover_title_summary_and_a_link()
    {
        var post = Posts.Make("Hello world", Posts.Utc(2026, 11, 5, 2), "A short summary of the post.", "https://example.com/media/a.jpg?width=600&height=315&rmode=crop");

        RenderedDigest mail = DigestRenderer.Render(Model([post]));

        Assert.Contains("href=\"https://example.com/blog/hello-world/\"", mail.Html);
        Assert.Contains(">Hello world</a>", mail.Html);
        Assert.Contains("A short summary of the post.", mail.Html);
        Assert.Contains("<img src=\"https://example.com/media/a.jpg?width=600&amp;height=315&amp;rmode=crop\"", mail.Html);
        Assert.Contains("5 Nov 2026", mail.Html);
        Assert.Contains("Read the post", mail.Html);
    }

    [Fact]
    public void A_post_without_a_cover_has_no_image()
    {
        var post = Posts.Make("No image", Posts.Utc(2026, 11, 5), cover: null);

        Assert.DoesNotContain("<img", DigestRenderer.Render(Model([post])).Html);
    }

    [Fact]
    public void Html_is_table_based_with_inline_styles_and_dark_text_on_white()
    {
        RenderedDigest mail = DigestRenderer.Render(Model([Posts.Make("One", Posts.Utc(2026, 11, 5))]));

        Assert.Contains("<table role=\"presentation\"", mail.Html);
        Assert.Contains("bgcolor=\"#ffffff\"", mail.Html);
        Assert.Contains("color:#1c2321", mail.Html);
        Assert.Contains("style=\"", mail.Html);
        Assert.DoesNotContain("<style", mail.Html);
        Assert.DoesNotContain("<script", mail.Html);
        Assert.DoesNotContain("display:flex", mail.Html);
    }

    [Fact]
    public void Text_and_markup_in_a_title_or_summary_is_escaped()
    {
        var post = Posts.Make("<script>alert(1)</script> & more", Posts.Utc(2026, 11, 5), "<p>Hello <b>there</b> &amp; welcome</p><script>x()</script>");

        RenderedDigest mail = DigestRenderer.Render(Model([post]));

        Assert.DoesNotContain("<script>", mail.Html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt; &amp; more", mail.Html);
        Assert.Contains("Hello there &amp; welcome", mail.Html);
        Assert.DoesNotContain("x()", mail.Html);
    }

    [Fact]
    public void The_summary_is_cut_to_the_limit()
    {
        var post = Posts.Make("Long", Posts.Utc(2026, 11, 5), new string('a', 50) + " " + new string('b', 400));
        var model = Model([post]) with { SummaryMaxLength = 100 };

        string text = DigestRenderer.Render(model).Text;

        Assert.Contains("...", text);
        Assert.DoesNotContain(new string('b', 200), text);
    }

    [Fact]
    public void Unsubscribe_link_and_one_click_headers_are_present()
    {
        RenderedDigest mail = DigestRenderer.Render(Model([Posts.Make("One", Posts.Utc(2026, 11, 5))]));

        Assert.Contains($"href=\"{Unsub}\"", mail.Html);
        Assert.Contains("Unsubscribe: " + Unsub, mail.Text);
        Assert.Equal("<" + Unsub + ">", mail.Headers["List-Unsubscribe"]);
        Assert.Equal("List-Unsubscribe=One-Click", mail.Headers["List-Unsubscribe-Post"]);
    }

    [Fact]
    public void Links_that_are_not_http_or_https_are_dropped()
    {
        var bad = Posts.Make("Bad", Posts.Utc(2026, 11, 5), url: "javascript:alert(1)");
        var good = Posts.Make("Good", Posts.Utc(2026, 11, 4));
        var model = Model([bad, good]) with { UnsubscribeUrl = "javascript:alert(2)" };

        RenderedDigest mail = DigestRenderer.Render(model);

        Assert.DoesNotContain("javascript:", mail.Html);
        Assert.DoesNotContain("Bad", mail.Html);
        Assert.Contains("Good", mail.Html);
        Assert.Empty(mail.Headers);
    }

    [Fact]
    public void Subject_names_the_post_when_there_is_one_and_the_count_when_there_are_more()
    {
        Assert.Equal("New post: First", DigestRenderer.Render(Model([Posts.Make("First", Posts.Utc(2026, 11, 5))])).Subject);
        Assert.Equal("2 new posts on Example", DigestRenderer.Render(Model([Posts.Make("A", Posts.Utc(2026, 11, 5)), Posts.Make("B", Posts.Utc(2026, 11, 4))])).Subject);
        Assert.StartsWith("[Test] ", DigestRenderer.Render(Model([Posts.Make("A", Posts.Utc(2026, 11, 5))], test: true)).Subject);
    }

    [Fact]
    public void A_subject_never_carries_a_line_break()
    {
        RenderedDigest mail = DigestRenderer.Render(Model([Posts.Make("Line\r\nBcc: x@example.com", Posts.Utc(2026, 11, 5))]));

        Assert.DoesNotContain('\r', mail.Subject);
        Assert.DoesNotContain('\n', mail.Subject);
    }

    [Fact]
    public void Plain_text_lists_title_date_summary_and_link()
    {
        string text = DigestRenderer.Render(Model([Posts.Make("Hello world", Posts.Utc(2026, 11, 5, 2), "About hello.")])).Text;

        Assert.Contains("1. Hello world (5 Nov 2026)", text);
        Assert.Contains("About hello.", text);
        Assert.Contains("https://example.com/blog/hello-world/", text);
        Assert.DoesNotContain("<", text);
    }

    [Fact]
    public void Dates_use_the_site_time_zone()
    {
        // 23:00 UTC on 5 Nov is 07:00 on 6 Nov in Manila.
        string text = DigestRenderer.Render(Model([Posts.Make("Late", Posts.Utc(2026, 11, 5, 23))])).Text;

        Assert.Contains("(6 Nov 2026)", text);
    }
}
