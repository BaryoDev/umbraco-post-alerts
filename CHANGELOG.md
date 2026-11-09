# Changelog

## 0.1.0

First release for Umbraco 17. Readers can subscribe to new-post alerts two ways: a weekly email digest after double opt-in, or a browser push when a post is first published. Digest mail goes out through MailKit with Umbraco's SMTP settings as multipart/alternative with List-Unsubscribe and one-click headers, with a fallback to `IEmailSender`. Includes a sign-up view component that works without JavaScript for email, a backoffice dashboard with a test digest, and posts under Public Access are never included.
