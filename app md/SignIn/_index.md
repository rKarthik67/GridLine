# Sign in — ticket index

- `01-email-password-sign-in/` — Email/password login form on `/signin`, `POST /auth/login`, token issuance, and post-login redirect to the `next` destination.
- `02-session-restore-on-load/` — Silent re-authentication on app load via stored token + `GET /me`, and the `Private` route guard's loading/redirect behavior.
- `03-sign-out/` — Signing out revokes the server-side token (`POST /auth/logout`) and propagates to every other open browser tab via the `storage` event.
- `04-capability-based-access/` — How roles/capabilities are resolved from `UserRole`/`AccessRole`/`RoleGrant` at login and session restore, and the `can()`/`Can()`/`Require()` checks they drive.
