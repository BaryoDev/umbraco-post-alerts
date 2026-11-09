using BaryoDev.Umbraco.PostAlerts.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Configuration.Models;

namespace BaryoDev.Umbraco.PostAlerts;

public sealed record PostAlertsModel(
    string Heading,
    string Intro,
    bool ShowEmail,
    bool ShowPush,
    string ReturnUrl,
    string? Notice,
    bool IsError,
    string PushKeyUrl,
    string PushSubscriptionUrl,
    string ServiceWorkerUrl);

/// <summary>Usage: <c>@await Component.InvokeAsync("PostAlerts")</c>. Renders nothing when neither channel is configured.</summary>
[ViewComponent(Name = "PostAlerts")]
public sealed class PostAlertsViewComponent(
    IOptionsMonitor<PostAlertsOptions> options,
    IOptionsMonitor<GlobalSettings> global) : ViewComponent
{
    internal static readonly IReadOnlyDictionary<string, string> Messages = new Dictionary<string, string>
    {
        ["sent"] = "Thanks. If the address is new, a confirmation email is on its way. Open the link in it to finish.",
        ["error-invalid"] = "That email address does not look right.",
        ["error-ratelimited"] = "Too many tries from your network. Try again later.",
        ["error-antiforgery"] = "The form expired. Reload the page and send it again.",
        ["error-unavailable"] = "Email sign-up is not available right now.",
    };

    public IViewComponentResult Invoke(string? heading = null, string? intro = null, bool email = true, bool push = true)
    {
        PostAlertsOptions o = options.CurrentValue;
        PostAlertsOptionsCheck.Result check = PostAlertsOptionsCheck.Check(o, global.CurrentValue.Smtp?.From);
        bool showEmail = email && check.EmailReady;
        bool showPush = push && check.PushReady;
        if (!showEmail && !showPush)
        {
            return Content(string.Empty);
        }

        string? code = Request.Query["bdpa"].FirstOrDefault();
        string? notice = code is not null && Messages.TryGetValue(code, out string? m) ? m : null;
        string returnUrl = Request.PathBase + Request.Path;

        return View(new PostAlertsModel(
            heading ?? "Get new posts",
            intro ?? DefaultIntro(showEmail, showPush),
            showEmail,
            showPush,
            returnUrl,
            notice,
            code?.StartsWith("error-", StringComparison.Ordinal) == true,
            Url.Content("~/baryo-post-alerts/push/key"),
            Url.Content("~/baryo-post-alerts/push/subscription"),
            string.IsNullOrWhiteSpace(o.ServiceWorkerPath) ? "/baryo-post-alerts/sw.js" : o.ServiceWorkerPath));
    }

    internal static string DefaultIntro(bool email, bool push) =>
        email && push ? "Get one email a week with new posts, or a notification in this browser when a post goes up."
        : email ? "Get one email a week with new posts. Nothing is sent in a week with none."
        : "Get a notification in this browser when a new post goes up.";
}
