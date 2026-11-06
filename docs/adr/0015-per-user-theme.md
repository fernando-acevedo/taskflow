# 15. The color theme is a per-user preference stored on the server

- Status: Accepted
- Date: 2026-11-06

## Context

The app needs a light and a dark theme. The choice can live in the browser only (`localStorage`) or on the user's
account. A browser-only choice is lost when the user signs in on another device, and the phase 7 multitenant work
must not decide it: the theme belongs to the person, not to the organization.

## Decision

Store it on the user, with three values: `System` (default, follows `prefers-color-scheme`), `Light` and `Dark`.
There is no high-contrast theme.

- `ApplicationUser.ThemePreference` is stored as a string column (`HasConversion<string>`, default `System`). The
  enum is `ThemePreference` in `Application/Users`; the port `IIdentityService.UpdateThemePreferenceAsync` writes it.
- `GET /api/users/me` returns it as `themePreference`. `PUT /api/users/me/preferences` with
  `{ "themePreference": "Dark" }` saves it and answers 204. A value outside the enum is a 400 (`IsInEnum`).
- Changing the theme is allowed in the public demo: it is not a credential, so `DemoRestrictionsFilter` does not
  close it.
- Frontend: the toolbar button opens a menu with the three values. The login page applies the value saved in the
  browser (so it is themed before the user is known); when a session starts (login or restore) the server value wins
  and is copied back to the browser. This also replaces a choice made on the login screen without a session: the
  account's value is the one the user set deliberately. The server value is applied once per session (by user id), so
  later changes to the user in the store (name, email) do not undo a newer choice; the store's copy is updated when
  the `PUT` succeeds.
- Changes are sent in order (one `PUT` at a time). If one fails, the user gets a notice and the theme goes back to
  the last value the server confirmed.
- It is independent of the tenant (phase 7): the preference is per user, not per organization.

## Consequences

- One extra column and no new table; the migration is generated separately.
- The first paint of the login page depends on the browser copy; a user on a new device sees `System` until signing in.
