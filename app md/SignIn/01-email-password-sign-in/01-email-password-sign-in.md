## Title
Sign in — Email/password sign-in

### Summary
The Sign in page lets a user authenticate with an email and password, receive a bearer token, and land back on the page they originally tried to reach (or on the project list). This is the entry point to the entire private app — every private route redirects here when the user has no session.

### User story
As a Gridline user, I want to sign in with my email and password so that I can access my projects and pick up exactly where a protected link sent me.

### Acceptance criteria
- [ ] `/signin` renders a form with an email field (pre-filled with `demo@northshore.example`), a password field, and a "Sign in" button; both fields are HTML-required and the email field uses `type="email"` with `autoComplete="username"`, the password field `type="password"` with `autoComplete="current-password"`.
- [ ] Submitting the form calls `signIn(email, password)` from `useAuth()`, which calls `POST /auth/login` with body `{ email, password }`.
- [ ] While the request is in flight the button is disabled and reads "Signing in…"; on success the button returns to its normal "Sign in" state (component unmounts on navigation, but the busy flag is also reset in a `finally` block).
- [ ] On success, the response `{ accessToken, me }` is stored: `accessToken` is written to `localStorage` key `gridline.token` via `tokenStore.set`, and `me` becomes the in-memory current user.
- [ ] On success, the user is navigated (replacing history) to the `next` query-string parameter of the current URL if present, otherwise to `/projects`.
- [ ] If the email does not match an active `AppUser` row, or the password does not match the stored hash, the backend returns `401` with message "Email or password is not right." and code `signin`; the page displays this message in a red `role="alert"` paragraph beneath the password field and does not navigate.
- [ ] Matching against `AppUser` is case-insensitive on email (`citext` column) and only considers rows where `IsActive` is true; inactive or deleted users cannot sign in even with a correct password.
- [ ] A successful login inserts a new `LoginToken` row (does not reuse/replace any existing token for that user) and updates `AppUser.LastSeenAt`; any previously issued tokens for that user remain valid until they individually expire or are revoked.
- [ ] Every request made by the SPA (login included) carries an `X-Tab-Id` header — a per-browser-tab UUID stored in `sessionStorage` under `gridline.tab` — used elsewhere for realtime echo suppression (see Architecture.md: Realtime).

### Technical spec
**Frontend:** `web/src/pages/SignIn.tsx` (`SignIn` component, local `submit` handler) and `web/src/app/auth.tsx` (`AuthProvider.signIn`). Reads `next` from `useSearchParams()`; navigates via `useNavigate()` with `{ replace: true }`. Calls `api.post<{ accessToken: string; me: Me }>('/auth/login', { email, password })` (`web/src/api/client.ts`); `Me` is defined in `web/src/api/types.ts` as `{ userId, name, initials, colour, email, roles: string[], capabilities: string[] }`. On success calls `tokenStore.set(r.accessToken)` (writes `localStorage['gridline.token']`) and `setMe(r.me)`. Errors are `ApiError` instances (`status`, `message`, `code`, `detail`); `message` is rendered directly.

**Backend:** `POST /auth/login` in `Endpoints/AuthEndpoints.cs` (`LoginIn(string Email, string Password)` record), mapped with `.AllowAnonymous()` — no auth/capability check, it is the entry point. Logic: opens a raw `Db` connection (not the `Work` unit-of-work — this endpoint does not go through `Work.Begin/Commit`, so it never logs an `ActivityEvent` and never emits a SignalR `changed` event); runs `SELECT UserId, PasswordHash FROM AppUser WHERE LoginEmail = @e::citext AND IsActive` with `@e` bound to `body.Email?.Trim()` (the submitted email is trimmed server-side before matching, password is not trimmed); if no row (`QueryFirstOrDefaultAsync` returns the default `(0, null)` tuple) or `Passwords.Verify(password, hash)` fails (`Infrastructure/Auth.cs`, PBKDF2-SHA256, 100k iterations, constant-time compare), throws `ApiException(401, "Email or password is not right.", "signin")`. On success: generates a 32-byte random token (`Tokens.New()`), inserts `LoginToken (UserId, TokenHash, ExpiresAt)` with `ExpiresAt = now() + interval '12 hours'` (only `Tokens.Hash(token)` — the SHA-256 digest — is persisted, never the raw token), updates `AppUser.LastSeenAt = now()`, then loads the full `CurrentUser` via `TokenAuthHandler.Load` and returns `{ accessToken: token, me }`.

**Database:** Reads/writes `gridline.AppUser` (`UserId`, `PasswordHash`, `LoginEmail` (`citext`, `UNIQUE`), `IsActive`, `LastSeenAt`) and inserts into `gridline.LoginToken` (`TokenId`, `UserId` FK to `AppUser` `ON DELETE CASCADE`, `TokenHash bytea UNIQUE`, `IssuedAt`, `ExpiresAt`, `RevokedAt`). The `CurrentUser` load join also reads `gridline.UserRole`, `gridline.AccessRole` (for `Roles`) and `gridline.RoleGrant` (for `Capabilities`) — see ticket 04. `AppUser` has trigger `TrgTouch BEFORE UPDATE ... EXECUTE FUNCTION gridline.TouchRow()`, so the `UPDATE AppUser SET LastSeenAt = now()` on login also sets `AppUser.ChangedAt = now()` and increments `AppUser.RowRev` as a side effect (`TouchRow()`: `NEW.ChangedAt := now(); NEW.RowRev := OLD.RowRev + 1`).

### Dependencies
- Ticket 02 (Session restore on app load) consumes the token this ticket stores.
- Ticket 03 (Sign out) revokes the `LoginToken` row this ticket creates.
- Ticket 04 (Capability-based access) — the `me.capabilities` returned here drives `can()` checks across the whole app.
- See Architecture.md: Auth (`TokenAuthHandler`, opaque bearer tokens) and Authorization/scoping for how the returned token is used on every subsequent request.

### Notes / edge cases
- This is one of the few mutating endpoints in the codebase that does **not** use the `Work` unit-of-work pattern — no transaction wrapper, no `ActivityEvent` row, no SignalR broadcast. A failed login attempt is not logged or rate-limited anywhere in this code path.
- Login never invalidates or replaces prior tokens; a user can be signed in from many devices/browsers simultaneously, each holding its own `LoginToken` row, all expiring independently 12 hours after issue.
- The password field's pre-filled default email (`demo@northshore.example`) is hard-coded in the component state initializer — a developer reproducing this screen must keep that default, not leave the field blank, to match current behavior.
- `AppUser.RowRev` increments on every login as a side effect of touching `LastSeenAt`; nothing currently depends on this, but any future feature using `AppUser.RowRev` for optimistic concurrency needs to account for it changing on every sign-in.
- Network failures (API unreachable) are surfaced by `client.ts` as `ApiError(0, 'Can’t reach the Gridline API. Is it running?', 'offline')` (note the typographic apostrophe in the source literal) and rendered the same way as a 401 — the user sees the same alert box for both cases, just different text.
