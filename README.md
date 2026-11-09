# BaryoDev.Umbraco.PostAlerts

New-post alerts for Umbraco 17. Readers pick a channel:

- A weekly email digest, sent only in weeks with at least one new post. Double opt-in.
- A browser push notification when a post is first published.

The email form works without JavaScript.

## Install

```
dotnet add package BaryoDev.Umbraco.PostAlerts
```

Start the site. The package creates its tables (`baryoPostAlertsSubscriber`, `baryoPostAlertsPush`, `baryoPostAlertsDigestRun`, `baryoPostAlertsPushed`) through a migration plan.

## Email: SMTP

The package sends through Umbraco's own email sender, so credentials stay in server config. Example for Resend over SMTP, with placeholders. Put the password in an environment variable or user secrets, not in a file you commit.

```json
{
  "Umbraco": {
    "CMS": {
      "Global": {
        "Smtp": {
          "From": "alerts@your-domain.example",
          "Host": "smtp.resend.com",
          "Port": 465,
          "SecureSocketOptions": "SslOnConnect",
          "Username": "resend",
          "Password": "<set from the environment>"
        }
      }
    }
  }
}
```

Environment variable form: `Umbraco__CMS__Global__Smtp__Password`.

## Options

Section `BaryoDev:PostAlerts`.

| Option | Default | Notes |
| --- | --- | --- |
| `DocumentTypes` | `["post"]` | Aliases that count as posts. |
| `SummaryAlias` | `summary` | Text or rich text. Markup is stripped. |
| `CoverAlias` | `["headerImage","cover"]` | First alias holding an image wins. |
| `DateAlias` | `publishDate` | Falls back to the publish date. |
| `IncludeProtected` | `false` | Posts under Public Access are left out unless this is true. |
| `EnableDigest` | `true` | Turns email sign-up and the digest on or off. |
| `SendDay` | `Monday` | |
| `SendHourLocal` | `9` | In `TimeZone`. |
| `TimeZone` | `Asia/Manila` | IANA id. |
| `SiteBaseUrl` | none | Required. Example `https://example.com`. Used for absolute links and image URLs. |
| `FromAddress` | Umbraco SMTP `From` | |
| `FromName` | none | Also used as the site name in emails. |
| `MaxPostsPerDigest` | `10` | 1 to 50. |
| `SummaryMaxLength` | `220` | |
| `CoverWidth`, `CoverHeight` | `600`, `315` | Height 0 keeps the ratio. |
| `ConfirmationHours` | `72` | How long a confirmation link works. |
| `ResendCooldownMinutes` | `5` | |
| `RateLimitMaxRequests`, `RateLimitWindowMinutes` | `5`, `10` | Per client address, for sign-up and push subscribe. |
| `VapidPublicKey`, `VapidPrivateKey`, `VapidSubject` | none | Turn push on. See below. |
| `PushEndpointHosts` | FCM, Mozilla, Windows, Apple | Only these push services are accepted. |
| `PushTimeToLiveHours` | `72` | |
| `PushMaxSubscriptions` | `50000` | |
| `ServiceWorkerPath` | `/baryo-post-alerts/sw.js` | Worker to register when the site has none. |

A wrong setting turns that channel off. It does not stop the site. The dashboard lists what is wrong.

## Browser push: VAPID keys

Generate a key pair once. With Node:

```
npx web-push generate-vapid-keys --json
```

Or from C#, using the package:

```csharp
var (publicKey, privateKey) = BaryoDev.Umbraco.PostAlerts.Push.VapidKeys.Generate();
```

Set them from the environment, never in a committed file:

```
BaryoDev__PostAlerts__VapidPublicKey=<public key>
BaryoDev__PostAlerts__VapidPrivateKey=<private key>
BaryoDev__PostAlerts__VapidSubject=mailto:you@your-domain.example
```

Keep the private key secret. Changing the key pair invalidates every stored subscription.

## Add the sign-up block

In a Razor template:

```cshtml
@await Component.InvokeAsync("PostAlerts")
```

Options: `heading`, `intro`, `email` (false hides the email form), `push` (false hides the push button). It renders nothing when neither channel is configured. The "Notify me in this browser" button stays hidden until the browser supports push, and the permission prompt appears only after the click.

Style it with custom properties on `.bdpa`: `--bdpa-ink`, `--bdpa-accent`, `--bdpa-bg`, `--bdpa-radius` and others in `post-alerts.css`. It also reads your site's `--ink`, `--green` and `--radius-card` when they exist.

Behind a proxy, enable forwarded headers so the rate limit sees the real address. On several servers, share the ASP.NET Core Data Protection keys: the antiforgery token and the unsubscribe links depend on them. Do not output-cache the part of the page with the form.

## Service worker

Push needs a service worker with `push` and `notificationclick` handlers.

If your site already has a worker (for example the one from BaryoDev.Umbraco.Pwa), add this line near the top of it:

```js
importScripts("/App_Plugins/BaryoDev.PostAlerts/push-sw.js");
```

If the site has no worker, do nothing. The button registers the one the package serves at `/baryo-post-alerts/sw.js`.

## How it works

- Sign up: `POST /baryo-post-alerts/subscribe`. The reply is the same whether or not the address is already subscribed. A confirmation email links to `GET /baryo-post-alerts/confirm/{token}`.
- Unsubscribe: `GET /baryo-post-alerts/unsubscribe/{token}` shows a confirm page, `POST` on the same address deletes the subscriber. The row is deleted, not kept.
- Digest: a recurring job checks every ten minutes. Each week is recorded in `baryoPostAlertsDigestRun`, so a restart never sends a week twice and a missed week is sent once, later, with both weeks of posts.
- Push: fires on the first publish of a post of a configured type. Subscriptions that answer 404 or 410 are deleted.
- Push endpoints: `GET /baryo-post-alerts/push/key`, `POST` and `DELETE /baryo-post-alerts/push/subscription`.

## Limits

Umbraco's email sender takes one body and no custom headers. So the digest is HTML only, and the unsubscribe link is in the footer. The `List-Unsubscribe` and `List-Unsubscribe-Post` headers are built and tested, but the default mailer cannot attach them. To send them, register your own `IAlertMailer` (it receives the headers and a plain-text version). The one-click POST endpoint already works.

Sites with several languages use the default language.

## Backoffice

Content section, "Post alerts" dashboard. Confirmed subscribers, push subscriptions, the last digest, the next digest due, and "Send test digest to me". The test email goes to your own backoffice address and does not count as a digest run. Subscriber addresses are not shown.

## Free and Pro

Everything here is free. Paid features, if any come later, will be separate packages unlocked by a domain key. Nothing released free will move behind it.

## TODO

The `icon.png` is a placeholder copied from another BaryoDev package. Replace it before publishing.

## License

MPL-2.0
