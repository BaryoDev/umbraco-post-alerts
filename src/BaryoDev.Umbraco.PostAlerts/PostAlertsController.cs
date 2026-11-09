using BaryoDev.Umbraco.PostAlerts.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace BaryoDev.Umbraco.PostAlerts;

public sealed record MessagePage(string Title, string Message, bool Ok, string HomeUrl);

public sealed record UnsubscribePage(string Token, string HomeUrl);

/// <summary>
/// Public email endpoints: sign-up, confirmation and unsubscribe. Every response is no-store, and the pages that
/// carry a token send no referrer.
/// </summary>
[Route("baryo-post-alerts")]
public sealed class PostAlertsController(
    SubscriptionService subscriptions,
    IAntiforgery antiforgery,
    IOptionsMonitor<PostAlertsOptions> options) : Controller
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        base.OnActionExecuting(context);
    }

    [HttpPost("subscribe")]
    public async Task<IActionResult> Subscribe(
        [FromForm] string? email,
        [FromForm] string? returnUrl,
        [FromForm(Name = "website")] string? honeypot)
    {
        bool json = WantsJson();
        string code;
        try
        {
            await antiforgery.ValidateRequestAsync(HttpContext);
            SubscribeResult result = await subscriptions.SubscribeAsync(
                new SubscribeInput(email, honeypot, ClientKey.From(HttpContext.Connection.RemoteIpAddress)), HttpContext.RequestAborted);
            code = Code(result);
        }
        catch (AntiforgeryValidationException)
        {
            code = "error-antiforgery";
        }

        if (json)
        {
            return code switch
            {
                "sent" => Ok(new { ok = true, code }),
                "error-ratelimited" => StatusCode(StatusCodes.Status429TooManyRequests, new { ok = false, code }),
                "error-unavailable" => NotFound(new { ok = false, code }),
                _ => BadRequest(new { ok = false, code }),
            };
        }

        string target = !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/";
        int cut = target.IndexOfAny(['?', '#']);
        string path = cut < 0 ? target : target[..cut];
        return LocalRedirect($"{path}?bdpa={code}#post-alerts");
    }

    [HttpGet("confirm/{token}")]
    public IActionResult Confirm(string token)
    {
        bool ok = subscriptions.Confirm(token);
        return View("Message", ok
            ? new MessagePage("You are subscribed", "Thanks. Your address is confirmed. You will get one email a week, and only when there is a new post.", true, Home())
            : new MessagePage("This link does not work", "It may have expired or been used already. Sign up again to get a new link.", false, Home()));
    }

    [HttpGet("unsubscribe/{token}")]
    public IActionResult UnsubscribePage(string token) =>
        View("Unsubscribe", new UnsubscribePage(token, Home()));

    /// <summary>
    /// Removes the subscriber. The token is the secret, so this needs no antiforgery token. That also lets the
    /// RFC 8058 one-click POST from a mail client work: it carries no cookie and no form token.
    /// </summary>
    [HttpPost("unsubscribe/{token}")]
    [IgnoreAntiforgeryToken]
    public IActionResult Unsubscribe(string token)
    {
        bool ok = subscriptions.Unsubscribe(token);
        if (!Request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase))
        {
            return ok ? Ok() : NotFound();
        }
        return View("Message", ok
            ? new MessagePage("You are unsubscribed", "Your address has been deleted. You will not get more emails.", true, Home())
            : new MessagePage("This link does not work", "The unsubscribe link is not valid.", false, Home()));
    }

    private bool WantsJson() => Request.Headers.Accept.ToString().Contains("application/json", StringComparison.OrdinalIgnoreCase);

    private string Home() => AbsoluteUrl.Root(options.CurrentValue.SiteBaseUrl) is { Length: > 0 } root ? root + "/" : Url.Content("~/");

    private static string Code(SubscribeResult result) => result switch
    {
        SubscribeResult.Accepted => "sent",
        SubscribeResult.Invalid => "error-invalid",
        SubscribeResult.RateLimited => "error-ratelimited",
        _ => "error-unavailable",
    };
}
