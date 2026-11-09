using BaryoDev.Umbraco.PostAlerts.Push;

namespace BaryoDev.Umbraco.PostAlerts.Tests;

public class OptionsCheckTests
{
    [Fact]
    public void The_defaults_match_the_documented_values()
    {
        var o = new PostAlertsOptions();

        Assert.Equal(["post"], o.DocumentTypes);
        Assert.Equal("summary", o.SummaryAlias);
        Assert.Equal(["headerImage", "cover"], o.CoverAlias);
        Assert.Equal("publishDate", o.DateAlias);
        Assert.Equal(DayOfWeek.Monday, o.SendDay);
        Assert.Equal(9, o.SendHourLocal);
        Assert.Equal("Asia/Manila", o.TimeZone);
        Assert.Equal("BaryoDev:PostAlerts", PostAlertsOptions.Section);
    }

    [Fact]
    public void A_valid_setup_has_email_ready_and_push_off_until_keys_are_set()
    {
        var result = PostAlertsOptionsCheck.Check(TestOptions.Valid());

        Assert.True(result.EmailReady);
        Assert.False(result.PushReady);
        Assert.Contains(result.Push, p => p.Contains("not set"));
    }

    [Fact]
    public void Generated_keys_make_push_ready()
    {
        var result = PostAlertsOptionsCheck.Check(TestOptions.WithPush());

        Assert.True(result.PushReady);
        Assert.Empty(result.Push);
    }

    [Fact]
    public void A_missing_site_address_turns_email_off()
    {
        PostAlertsOptions o = TestOptions.Valid();
        o.SiteBaseUrl = "";

        var result = PostAlertsOptionsCheck.Check(o);

        Assert.False(result.EmailReady);
        Assert.Contains(result.Email, p => p.Contains("SiteBaseUrl"));
    }

    [Theory]
    [InlineData("example.com")]
    [InlineData("ftp://example.com")]
    [InlineData("/relative")]
    public void The_site_address_must_be_absolute_http_or_https(string value)
    {
        PostAlertsOptions o = TestOptions.Valid();
        o.SiteBaseUrl = value;

        Assert.False(PostAlertsOptionsCheck.Check(o).EmailReady);
    }

    [Fact]
    public void An_unknown_time_zone_turns_email_off()
    {
        PostAlertsOptions o = TestOptions.Valid();
        o.TimeZone = "Mars/Olympus";

        Assert.Contains(PostAlertsOptionsCheck.Check(o).Email, p => p.Contains("TimeZone"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(24)]
    public void The_send_hour_must_be_0_to_23(int hour)
    {
        PostAlertsOptions o = TestOptions.Valid();
        o.SendHourLocal = hour;

        Assert.Contains(PostAlertsOptionsCheck.Check(o).Email, p => p.Contains("SendHourLocal"));
    }

    [Fact]
    public void Document_types_cannot_be_empty()
    {
        PostAlertsOptions o = TestOptions.WithPush();
        o.DocumentTypes = [];

        var result = PostAlertsOptionsCheck.Check(o);

        Assert.False(result.EmailReady);
        Assert.False(result.PushReady);
    }

    [Fact]
    public void The_from_address_falls_back_to_the_umbraco_smtp_setting()
    {
        PostAlertsOptions o = TestOptions.Valid();
        o.FromAddress = "";

        Assert.False(PostAlertsOptionsCheck.Check(o).EmailReady);
        Assert.True(PostAlertsOptionsCheck.Check(o, "smtp@example.com").EmailReady);
        Assert.Equal("smtp@example.com", PostAlertsOptionsCheck.EffectiveFrom(o, "smtp@example.com"));
        Assert.Null(PostAlertsOptionsCheck.EffectiveFrom(o, "not an address"));
    }

    [Fact]
    public void Digest_off_means_email_not_ready()
    {
        PostAlertsOptions o = TestOptions.Valid();
        o.EnableDigest = false;

        Assert.False(PostAlertsOptionsCheck.Check(o).EmailReady);
    }

    [Fact]
    public void Partial_vapid_settings_are_reported()
    {
        PostAlertsOptions o = TestOptions.WithPush();
        o.VapidPrivateKey = "";

        var result = PostAlertsOptionsCheck.Check(o);

        Assert.False(result.PushReady);
        Assert.Contains(result.Push, p => p.Contains("VapidPrivateKey"));
    }

    [Theory]
    [InlineData("not-a-key")]
    [InlineData("AAAA")]
    [InlineData("BAAA+/==")]
    public void A_public_key_of_the_wrong_shape_is_reported(string key)
    {
        PostAlertsOptions o = TestOptions.WithPush();
        o.VapidPublicKey = key;

        Assert.Contains(PostAlertsOptionsCheck.Check(o).Push, p => p.Contains("VapidPublicKey"));
    }

    [Theory]
    [InlineData("owner@example.com")]
    [InlineData("http://example.com")]
    [InlineData("mailto:")]
    [InlineData("")]
    public void The_subject_must_be_mailto_or_https(string subject)
    {
        PostAlertsOptions o = TestOptions.WithPush();
        o.VapidSubject = subject;

        Assert.Contains(PostAlertsOptionsCheck.Check(o).Push, p => p.Contains("VapidSubject") || p.Contains("not set"));
        Assert.False(PostAlertsOptionsCheck.Check(o).PushReady);
    }

    [Theory]
    [InlineData("mailto:owner@example.com")]
    [InlineData("https://example.com/contact")]
    public void A_mailto_or_https_subject_is_accepted(string subject)
    {
        PostAlertsOptions o = TestOptions.WithPush();
        o.VapidSubject = subject;

        Assert.True(PostAlertsOptionsCheck.Check(o).PushReady);
    }

    [Fact]
    public void Generated_vapid_keys_have_the_right_sizes()
    {
        var (pub, priv) = VapidKeys.Generate();

        Assert.Equal(65, Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(pub).Length);
        Assert.Equal(0x04, Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(pub)[0]);
        Assert.Equal(32, Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(priv).Length);
        Assert.DoesNotContain('=', pub + priv);
        Assert.NotEqual(pub, VapidKeys.Generate().PublicKey);
    }

    [Fact]
    public void The_library_signs_a_vapid_header_with_the_generated_keys()
    {
        var (pub, priv) = VapidKeys.Generate();
        using var vapid = new Lib.Net.Http.WebPush.Authentication.VapidAuthentication(pub, priv) { Subject = "mailto:owner@example.com" };

        string header = vapid.GetVapidSchemeAuthenticationHeaderValueParameter("https://fcm.googleapis.com");

        Assert.Contains("t=", header);
        Assert.Contains("k=" + pub, header);
    }
}
