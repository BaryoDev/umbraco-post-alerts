# Changelog

## Unreleased

- A digest run where every send fails (SMTP down) is marked failed and its period is retried on the next tick. Partly failed runs still count as sent.
- Subscribe queues the confirmation email, so a new, pending and confirmed address get the same reply in the same time.
- Push subscribe checks that the key is a point on the P-256 curve. A subscription whose key throws a cryptographic error is deleted on the next send. Existing databases get a `Status` column on the pushed-post table.
- A push is marked queued at publish and sent after delivery. Queued pushes are queued again at start.
- The digest asks for the posts of the period, and loads the publish date of posts without the date property in one query.
- The test digest error no longer shows the mail server message, and `TextCleaner` has a match timeout.
- Removed the stale icon TODO from the README.

## 0.1.0

First release for Umbraco 17. Readers can subscribe to new-post alerts two ways: a weekly email digest after double opt-in, or a browser push when a post is first published. Digest mail goes out through MailKit with Umbraco's SMTP settings as multipart/alternative with List-Unsubscribe and one-click headers, with a fallback to `IEmailSender`. Includes a sign-up view component that works without JavaScript for email, a backoffice dashboard with a test digest, and posts under Public Access are never included.
