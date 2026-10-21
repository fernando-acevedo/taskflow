# 8. Admins invite users by email instead of setting their password

- Status: Accepted
- Date: 2026-10-20

## Context

Admins need to add people to TaskFlow without asking them to register first. A first version let the admin type
an initial password, which means the admin knows it and has to pass it on outside the app. The app had no way
to send email.

## Decision

- `POST /api/admin/users` creates a confirmed account **without a password** and emails the user a link to
  `/accept-invitation?email=…&token=…` in the frontend. There the user chooses and confirms a password (with a
  strength meter) and is signed in. `POST /api/admin/users/{id}/invitation` sends a new link while the user
  hasn't chosen a password; `UserDto.IsInvitationPending` is true until then.
- The token comes from a dedicated Identity token provider (`InvitationTokenProvider`, a
  `DataProtectorTokenProvider`) so it can live longer than password-reset tokens: 7 days by default,
  `Auth:InvitationDays`. It's bound to the security stamp, so setting the password makes it unusable again.
- `POST /api/invitations/accept` is anonymous and shares the per-IP rate limit of `/api/auth`. An unknown
  email, a wrong or expired token and an account that already has a password all get the same
  `Users.InvitationInvalid` error, so the endpoint doesn't reveal which emails exist.
- Email goes out over SMTP with MailKit, configured under `Email` (host, port, security, credentials, sender,
  frontend URL). The SMTP password is a secret: user-secrets locally, app settings in Azure. Without an SMTP
  host, `LoggingEmailSender` writes the email (with its link) to the log when `Email:LogWhenSmtpIsMissing` is
  true, which only `appsettings.Development.json` sets; elsewhere sending fails.
- If the email can't be sent, the account is still created and the API answers `Users.InvitationNotSent`
  (409) so the admin can fix the settings and resend.

## Consequences

- Admins never know users' passwords, and the same flow covers resending an expired link.
- SMTP is a new runtime dependency that phase 5 has to configure (any provider: Azure Communication Services,
  SendGrid, Microsoft 365, Gmail).
- Integration tests replace `IEmailSender` with an in-memory sender and follow the link from the captured email.
- The invitation text is in English and Spanish, chosen from the admin's request language; the invitee's own
  language isn't known yet.
