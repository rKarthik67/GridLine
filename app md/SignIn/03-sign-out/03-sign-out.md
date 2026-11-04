## Title
Sign in — Sign out (single tab and cross-tab)

### Summary
Signing out revokes the user's server-side token and clears the locally stored credential, and — because the access token is shared by every browser tab for that browser profile — propagates instantly to every other open tab so none of them keep acting as a signed-in session.

### User story
As a user who clicks sign out, I want my session ended everywhere in my browser (not just the tab I clicked in) so that a shared or public machine doesn't leave another tab still signed in as me.

### Acceptance criteria
- [ ] Calling `signOut()` sends `POST /auth/logout` with no body; regardless of whether that call succeeds or fails (it is wrapped in `.catch(() => {})`), the local token is cleared and local `me` state is set to `null`.
- [ ] `POST /auth/logout` revokes the `LoginToken` row matching the bearer token sent in the `Authorization` header of that request by setting `RevokedAt = now()`; if no `Authorization: Bearer ...` header is present, the endpoint still responds `204 No Content` and does nothing.
- [ ] After sign-out, the cleared token (`localStorage.removeItem('gridline.token')`) means every subsequent API call from that tab omits the `Authorization` header, so any private route's next render (or next `Private` mount-time check) redirects to `/signin`.
- [ ] Clearing the token in one tab fires a browser `storage` event in every *other* open tab on the same origin; `AuthProvider` listens for `storage` events where `key === 'gridline.token'` and `newValue` is falsy, and in that case sets that other tab's in-memory `me` to `null` immediately (without waiting for that tab to make its own failing request).
- [ ] A revoked `LoginToken` can no longer authenticate: once `RevokedAt` is set, the `WHERE ... t.RevokedAt IS NULL` condition in the token-lookup query (used by both `TokenAuthHandler` and `/me`) excludes it, so any request still carrying that token gets treated as unauthenticated.

### Technical spec
**Frontend:** `web/src/app/auth.tsx` — `AuthProvider.signOut()` (`async signOut() { await api.post('/auth/logout').catch(() => {}); tokenStore.set(null); setMe(null) }`) and the mount-time `window.addEventListener('storage', onStorage)` handler described above. Calls `api.post('/auth/logout')` (`web/src/api/client.ts`) with no request body. Note: the Sign in page/component itself (`SignIn.tsx`) does not contain a sign-out control — the sign-out entry point lives elsewhere in the shell/navigation UI, but the end-to-end mechanism (token clear, revoke call, cross-tab propagation) is owned by the same `auth.tsx` module this page's sign-in flow depends on.

**Backend:** `POST /auth/logout` in `Endpoints/AuthEndpoints.cs`, mapped with `.AllowAnonymous()`. Handler reads the raw `Authorization` header off `HttpContext.Request.Headers`; if it starts with `"Bearer "`, it hashes the token (`Tokens.Hash`) and runs `UPDATE LoginToken SET RevokedAt = now() WHERE TokenHash = @h`. Always returns `204 No Content` (`Results.NoContent()`) — there is no error response even if the token doesn't match any row. This endpoint, like login, bypasses the `Work` unit-of-work: no transaction object, no `ActivityEvent` log entry, no SignalR broadcast.

**Database:** Writes `gridline.LoginToken.RevokedAt` (`timestamptz`, nullable) on the row whose `TokenHash` (`bytea`, `UNIQUE`) matches the hash of the presented token. No other table is touched. There is no DB trigger on `LoginToken` (it is not in the `TrgTouch` table list and has no `ChangedAt`/`RowRev` columns), so revocation is a plain, untracked `UPDATE`.

### Dependencies
- Depends on ticket 01 (Email/password sign-in) for the token being revoked and ticket 02 (Session restore) for how `me` state is populated/cleared and how routes react once it's `null`.
- See Architecture.md: Auth (opaque bearer tokens, `LoginToken.RevokedAt`) for why revocation is checked at the SQL layer rather than by deleting the row.

### Notes / edge cases
- Because logout always returns `204` even for a missing/garbage `Authorization` header or an already-revoked token, the frontend has no way to distinguish "logout succeeded" from "there was nothing to revoke" — and deliberately doesn't try to (the `.catch(() => {})` means even a network error is treated as a successful local sign-out).
- The cross-tab `storage` event only fires in *other* tabs, never the tab that made the change (this is a browser-level `localStorage` semantics, not application code) — the originating tab relies on its own `setMe(null)` call in `signOut()`, not the listener.
- Old `LoginToken` rows are never deleted, only flagged `RevokedAt` (or left to expire via `ExpiresAt`) — there is no cleanup/retention job for this table described anywhere in the backend conventions, so the table grows unboundedly with one row per login.
- A user signed in on multiple devices/browsers (each holding its own `LoginToken` from separate logins, per ticket 01) is only signed out of the single token revoked by this call — sign-out does not revoke all of that user's tokens, only the one used to issue the logout request.
