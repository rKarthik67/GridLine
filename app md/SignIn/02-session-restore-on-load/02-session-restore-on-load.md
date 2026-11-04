## Title
Sign in — Session restore on app load

### Summary
When the SPA loads (or is refreshed) with a previously stored bearer token, the app silently re-authenticates by fetching the current user instead of forcing a fresh sign-in, and gates every private route on this check so the right thing (spinner, sign-in redirect, or the page) renders while it's in flight.

### User story
As a returning user with a valid session token, I want the app to recognize me automatically on load so that I don't have to sign in again every time I open or refresh a tab.

### Acceptance criteria
- [ ] On mount, `AuthProvider` checks `tokenStore.get()` (reads `localStorage['gridline.token']`); if there is no stored token, it immediately sets `loading = false` and leaves `me` as `null` — no network call is made.
- [ ] If a token is stored, the app calls `GET /me` and, on success, sets `me` to the returned `Me` object; `loading` is set to `false` in a `finally` regardless of outcome.
- [ ] If `GET /me` fails (e.g. token expired, revoked, or invalid), the stored token is cleared (`tokenStore.set(null)`) and `me` stays `null` — the user is treated as signed out.
- [ ] While `loading` is `true`, every private route (wrapped in `Private` in `App.tsx`) renders a plain "Signing you in…" placeholder instead of the page or a redirect.
- [ ] Once loading completes, if `me` is still `null`, `Private` redirects to `/signin?next=<url-encoded current path + search>`, replacing the current history entry.
- [ ] Once loading completes and `me` is set, `Private` renders the wrapped page (`Portfolio`, `ProjectLayout`, etc.) directly.
- [ ] `/signin` and `/bid/:token` are not wrapped in `Private` and are reachable regardless of auth state.
- [ ] Any unmatched route (`*`) redirects to `/projects`, which itself is gated by `Private` and will bounce to `/signin` if the session check hasn't resolved to a signed-in user.

### Technical spec
**Frontend:** `web/src/app/auth.tsx` (`AuthProvider`'s mount `useEffect`) and `web/src/App.tsx` (`Private` component). Calls `api.get<Me>('/me')` (`web/src/api/client.ts`); every request automatically attaches `Authorization: Bearer <token>` from `tokenStore.get()` when a token is present, so this call authenticates implicitly via the stored token — the frontend never passes the token as an explicit argument. `Me` shape is `{ userId: number; name: string; initials: string; colour: string; email: string; roles: string[]; capabilities: string[] }` (`web/src/api/types.ts`). `Private`'s redirect target is built from `useLocation()` (`loc.pathname + loc.search`), URL-encoded into the `next` query parameter that ticket 01's sign-in flow reads back out.

**Backend:** `GET /me` in `Endpoints/AuthEndpoints.cs` — `api.MapGet("/me", (HttpContext ctx) => ctx.Me())`. No explicit auth attribute is set on this route (unlike `/auth/login`/`/auth/logout` which carry `.AllowAnonymous()`), so it runs under the default authentication requirement; the handler itself calls `HttpContext.Me()` (`Infrastructure/Auth.cs`, `UserExtensions.Me`), which reads `ctx.Items[typeof(CurrentUser)]` — populated by `TokenAuthHandler.HandleAuthenticateAsync` when the bearer token resolves to an active, unexpired, unrevoked `LoginToken` — and throws `ApiException(401, "Please sign in.", "signin")` if nothing authenticated the request. `TokenAuthHandler` caches a resolved token's `CurrentUser` in-memory for 30 seconds (`IMemoryCache`, key `tok:<token>`), so a revoked token can still authenticate for up to 30 seconds after revocation.

**Database:** No direct query runs for `/me` itself — it returns the `CurrentUser` already resolved by `TokenAuthHandler.Load` during request authentication, which runs `SELECT u.UserId, u.GivenName || ' ' || u.FamilyName AS Name, u.Initials, u.Colour, u.LoginEmail::text AS Email, ... FROM gridline.AppUser u JOIN gridline.LoginToken t ON t.UserId = u.UserId WHERE u.IsActive AND t.TokenHash = @h AND t.RevokedAt IS NULL AND t.ExpiresAt > now()`, plus correlated subqueries against `gridline.UserRole`/`gridline.AccessRole` (Roles) and `gridline.UserRole`/`gridline.RoleGrant` (Capabilities, deduplicated with `DISTINCT`). No row is written by this feature.

### Dependencies
- Depends on ticket 01 (Email/password sign-in) having stored a valid token in `localStorage`.
- Ticket 03 (Sign out) is what clears the token this feature reads; ticket 04 (Capability-based access) consumes the `roles`/`capabilities` this call returns.
- See Architecture.md: Auth (opaque bearer tokens, 30s cache) and Routing map (`Private` guard, `/signin?next=...`).

### Notes / edge cases
- There is no token-refresh mechanism anywhere in this flow: a `LoginToken` is valid for a fixed 12 hours from issue (set at login, see ticket 01) and is never extended by subsequent `/me` calls. A user mid-session will be silently signed out once the token expires, surfaced the next time any API call returns 401 (the `client.ts` 401 handler clears the token, but `me` in `AuthProvider` state is only cleared by the mount-time check or by the cross-tab `storage` listener in ticket 03 — a long-lived tab that never re-runs the mount effect can keep stale `me` state in memory until its next failed request triggers a reload/redirect at the route level).
- The 30-second server-side token cache means a token revoked by "sign out everywhere"-style admin action (if ever added) would still authenticate requests for up to 30 seconds on any server replica that had it cached.
- `Private`'s loading placeholder has no styling beyond `padding: 40` and is shown on every private route on every full page load/refresh, not just the first one after login — a developer should not assume this component only renders once per app lifetime.
