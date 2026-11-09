# Design

New-post alerts for Umbraco 17. Two channels: a weekly email digest and browser push.

## Owner decisions

- Two channels, email digest and Web Push.
- Weekly digest over SMTP, sent through Umbraco's `IEmailSender` so credentials stay in `Umbraco:CMS:Global:Smtp`.
- Each post in the email shows cover image, title and a short summary, linked to the post.
- Send only in weeks with at least one newly published post.
- Double opt-in, store a hash of the token only, honeypot, per-IP rate limit, never reveal if an address is subscribed.
- Web Push with VAPID keys from configuration. Push fires on the first publish only.
- Free and MPL-2.0. Pro features, if any, come later as separate packages by domain key.

## Data

| Table | Columns |
| --- | --- |
| `baryoPostAlertsSubscriber` | Id, Email (unique, lower case), Status (0 pending, 1 confirmed), TokenHash, CreatedUtc, ConfirmedUtc |
| `baryoPostAlertsPush` | Id, EndpointHash (unique), Endpoint, P256dh, Auth, CreatedUtc |
| `baryoPostAlertsDigestRun` | Id, PeriodStartUtc, PeriodEndUtc (unique), Status, PostCount, Recipients, Sent, StartedUtc, CompletedUtc |
| `baryoPostAlertsPushed` | ContentKey, MarkedUtc |

No IP address and no user agent is stored. Unsubscribing deletes the subscriber row. There is no soft delete, so there is nothing to anonymise. Pending rows nobody confirmed are deleted after `ConfirmationHours`.

## Digest schedule

A send moment is `SendDay` at `SendHourLocal` in `TimeZone`. A period ends at a send moment and starts at the end of the previous run. The job runs every ten minutes and asks `DigestSchedule.Due`. If the latest send moment has no run row, that period is due.

- A run row is inserted before sending. The unique period end makes a second insert fail, so a restart or a second server cannot send the week again.
- A missed week is caught up once: the next due period starts at the last run, so one email carries every post since.
- A period with no posts, or no confirmed subscribers, is recorded as skipped. Nothing is sent and the window moves on.
- A local hour that does not exist (spring forward) moves forward one hour. A repeated hour takes the first.
- The job runs on the single or scheduling publisher server only.

## Subscribe flow

1. `POST /baryo-post-alerts/subscribe` (antiforgery, honeypot `website`, per-client rate limit). JSON or redirect reply, same text either way.
2. New address: pending row, random 32 byte token, only its SHA-256 stored, confirmation email.
3. Pending address asking again: new token, new email, at most once per `ResendCooldownMinutes`.
4. Confirmed address asking again: nothing happens, same reply.
5. `GET /baryo-post-alerts/confirm/{token}` confirms and clears the hash.
6. Digest emails carry `/baryo-post-alerts/unsubscribe/{token}`. `GET` shows a page, `POST` deletes. The token is the subscriber id protected by ASP.NET Core Data Protection, so no secret is stored and the link works in old emails. The POST has no antiforgery check because the token is the secret, and that lets the RFC 8058 one-click POST work.

## Push

- Keys, subject and the allowed push hosts come from configuration.
- Library: Lib.Net.Http.WebPush 3.3.1 (MIT, last release 2025, targets net6.0 and up). It handles VAPID and aes128gcm.
- Subscribe validates the key lengths and that the endpoint is https on an allowed push service host. The server posts to stored endpoints, so this is the guard against using it to reach internal addresses.
- First publish: `ContentPublishedNotification` handler marks the content key in `baryoPostAlertsPushed`. A key already marked is ignored, so re-publishing or unpublish and publish again sends nothing. The marker joins the publish transaction.
- Sending runs on a background queue, so publishing never waits on push services. The address of the post is looked up after a short wait because the published cache refreshes just after the notification.
- At the first start with push on, every post already published is marked, so installing the package and re-publishing an old post sends nothing.
- 404 and 410 delete the subscription. Other errors keep it.

## Protected content

Posts under Umbraco Public Access are left out of the digest, the test digest and push, because titles, summaries and covers would reach every subscriber. `IncludeProtected` (default false) turns that off.

## Email rendering

Table based, inline styles, 600 wide, dark text on white, no CSS in a style block, no script. Cover images use the image URL generator at `CoverWidth` by `CoverHeight` with crop, then become absolute with `SiteBaseUrl`. Everything from content is HTML encoded and only http and https links are written.

`IEmailSender` takes one body and no custom headers. So by default:

- The digest is HTML only. The plain-text version is built and tested for mailers that can use it.
- `List-Unsubscribe` and `List-Unsubscribe-Post` are built and tested but not attached. The footer link is the working unsubscribe.
- `IAlertMailer` is the seam. A site can register its own to send headers and multipart.

## Builder calls

These were not set by the owner.

- No `Directory.Packages.props`. The sibling packages do not use central package management, so this follows them.
- Unsubscribe token uses Data Protection instead of a stored hash, because the database cannot give back a raw token for the email. Sites on several servers must share the keys.
- Confirmation opens on GET as asked. A mail scanner that follows links can confirm an address. The cost is one unwanted but still double-checked subscription, and the unsubscribe link is in every email.
- Run claim is at most once. A crash after the claim and before the sends finish leaves the row `Started` and that week is not retried. A duplicate email was judged worse than a missed one.
- Dates: the `DateAlias` value with no time zone is read in `TimeZone`. A publish date with no kind is read as UTC. Check this on your site.
- Multi-language sites use the default language only.
- Cover images are not converted to JPEG. Some Outlook versions cannot show WebP, so use JPEG or PNG covers if your readers use them.
- The standalone service worker at `/baryo-post-alerts/sw.js` is served with `Service-Worker-Allowed: /` so it can cover the whole site when the site has no worker.
- Pending subscribers and the digest job run only when the options check passes. A wrong setting disables the channel and shows in the dashboard instead of stopping startup.
- Test digest posts: the last period, or the newest three if that period had none.
- The icon is a placeholder.

## Not in 0.1.0

- Per-category alerts, daily or instant email
- Multipart mail and the List-Unsubscribe headers with the default mailer
- Subscriber list export or import
- Pro features by domain key
