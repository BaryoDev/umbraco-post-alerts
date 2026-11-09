namespace BaryoDev.Umbraco.PostAlerts;

/// <summary>Settings under <c>BaryoDev:PostAlerts</c>. Secrets are read from configuration or the environment, never from code.</summary>
public sealed class PostAlertsOptions
{
    public const string Section = "BaryoDev:PostAlerts";

    // Content

    /// <summary>Document type aliases that count as posts.</summary>
    public string[] DocumentTypes { get; set; } = ["post"];
    public string SummaryAlias { get; set; } = "summary";
    /// <summary>Tried in order. The first alias that holds an image is the cover.</summary>
    public string[] CoverAlias { get; set; } = ["headerImage", "cover"];
    /// <summary>Property with the post date. When it is empty or missing, the publish date is used.</summary>
    /// <summary>Off by default: posts under Umbraco Public Access are left out of email and push, so their titles never reach subscribers.</summary>
    public bool IncludeProtected { get; set; }
    public string DateAlias { get; set; } = "publishDate";

    // Email digest

    /// <summary>Turns the email sign-up and the weekly digest on or off.</summary>
    public bool EnableDigest { get; set; } = true;
    public DayOfWeek SendDay { get; set; } = DayOfWeek.Monday;
    public int SendHourLocal { get; set; } = 9;
    /// <summary>IANA time zone id, for example Asia/Manila.</summary>
    public string TimeZone { get; set; } = "Asia/Manila";
    /// <summary>Public address of the site, for example https://example.com. Needed for absolute links and image URLs.</summary>
    public string SiteBaseUrl { get; set; } = "";
    /// <summary>Falls back to <c>Umbraco:CMS:Global:Smtp:From</c> when empty.</summary>
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "";
    public int MaxPostsPerDigest { get; set; } = 10;
    public int SummaryMaxLength { get; set; } = 220;
    public int CoverWidth { get; set; } = 600;
    /// <summary>Cover height in pixels. 0 keeps the original ratio.</summary>
    public int CoverHeight { get; set; } = 315;

    // Sign-up

    public int ConfirmationHours { get; set; } = 72;
    /// <summary>A pending address gets a new confirmation email at most this often.</summary>
    public int ResendCooldownMinutes { get; set; } = 5;
    public int RateLimitMaxRequests { get; set; } = 5;
    public int RateLimitWindowMinutes { get; set; } = 10;

    // Browser push

    public string VapidPublicKey { get; set; } = "";
    public string VapidPrivateKey { get; set; } = "";
    /// <summary>A mailto: or https: contact for the push services, for example mailto:you@example.com.</summary>
    public string VapidSubject { get; set; } = "";
    /// <summary>Push services allowed as subscription endpoints. A host matches when it equals an entry or ends with a dot and the entry.</summary>
    public string[] PushEndpointHosts { get; set; } = ["fcm.googleapis.com", "push.services.mozilla.com", "notify.windows.com", "push.apple.com"];
    public int PushTimeToLiveHours { get; set; } = 72;
    public int PushMaxSubscriptions { get; set; } = 50000;
    /// <summary>Path of the service worker to register when the site has none. The package serves one at this path.</summary>
    public string ServiceWorkerPath { get; set; } = "/baryo-post-alerts/sw.js";
}
