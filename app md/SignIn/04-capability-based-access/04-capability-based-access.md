## Title
Sign in — Capability-based access resolution

### Summary
Every sign-in (and session restore) resolves the user's roles and fine-grained capabilities from the database and attaches them to the session, giving the rest of the app a single `can(capability)` check to enable or hide features without each page re-deriving permissions.

### User story
As a signed-in user, I want the features I can access to be determined by my assigned role(s) so that I only see and can use the actions I'm actually permitted to perform.

### Acceptance criteria
- [ ] Both `POST /auth/login` and `GET /me` return a `me` object whose `roles` and `capabilities` arrays are computed fresh from the database at the time of the call (via `TokenAuthHandler.Load`), not cached on the `AppUser` row itself.
- [ ] `roles` is the distinct set of `AccessRole.RoleCode` values for every `AccessRole` the user holds via `UserRole`; `capabilities` is the distinct set of `RoleGrant.Capability` strings granted to any of those roles. A user with no `UserRole` rows gets empty arrays for both (`coalesce(..., ARRAY[]::text[])`), not `null`.
- [ ] `useAuth().can(cap)` returns `true` if `me` is non-null and either `me.capabilities` includes the literal string `"*"` or includes `cap` exactly; it returns `false` if there is no signed-in user at all.
- [ ] Server-side, `CurrentUser.Can(cap)` (used by protected endpoints across the app) applies the identical rule: `Capabilities.Contains("*") || Capabilities.Contains(cap)`; `CurrentUser.IsAdmin` is `Capabilities.Contains("*")`.
- [ ] `CurrentUser.Require(cap)` throws a `403 Forbidden` (`ApiException.Forbidden`, message `"You need the '{cap}' permission for this."`) when `Can(cap)` is false; this is the mechanism every non-anonymous mutating endpoint in the app uses to gate access beyond plain authentication.
- [ ] A capability of exactly `"*"` (the literal asterisk string) acts as an admin override and satisfies every `Can`/`can` check regardless of the specific capability string requested.

### Technical spec
**Frontend:** `web/src/app/auth.tsx` — `AuthState.can(cap)` on the `AuthProvider` context value; consuming components call `useAuth().can('some.capability')` to conditionally render or enable UI (pattern used across private pages, not specific markup on `SignIn.tsx` itself since the capability set only becomes meaningful once `me` is populated, i.e. after ticket 01/02). `Me.roles` and `Me.capabilities` (both `string[]`) are defined in `web/src/api/types.ts` and populated by the `POST /auth/login` and `GET /me` responses documented in tickets 01 and 02.

**Backend:** No dedicated endpoint — the computation lives in `TokenAuthHandler.Load` (`Infrastructure/Auth.cs`), invoked by both `/auth/login` (ticket 01) and implicitly by `TokenAuthHandler.HandleAuthenticateAsync` on every authenticated request (backing `/me`, ticket 02). `CurrentUser` (`record CurrentUser(long UserId, string Name, string Initials, string Colour, string Email, string[] Roles, string[] Capabilities)`) exposes `Can(string cap)`, `IsAdmin`, and `Require(string cap)`, used throughout `Endpoints/*.cs` as `ctx.Me().Require(...)` / `ctx.Me().Can(...)` guard checks (see Architecture.md: Authorization/scoping for how this combines with per-project `Access.Check` scope resolution).

**Database:** Reads (no writes) `gridline.UserRole` (`UserId`, `RoleId`, composite PK `(UserId, RoleId)`, FK `UserId → AppUser ON DELETE CASCADE`, FK `RoleId → AccessRole`) joined to `gridline.AccessRole` (`RoleId smallint IDENTITY PK`, `RoleCode text UNIQUE`, `RoleName`, `Summary`) for `Roles`, and to `gridline.RoleGrant` (`RoleId → AccessRole ON DELETE CASCADE`, `Capability text`, composite PK `(RoleId, Capability)`) for `Capabilities`. Both are built with correlated-subquery `array_agg(...)` against the single `AppUser` row being loaded (`TokenAuthHandler.Load`): `Capabilities` explicitly uses `array_agg(DISTINCT g.Capability)` because the same capability string can be granted by more than one role a user holds; `Roles` uses a plain `array_agg(r.RoleCode)` without `DISTINCT` — it doesn't need one because `UserRole`'s composite primary key `(UserId, RoleId)` already makes each role appear at most once per user.

### Dependencies
- Depends on ticket 01 (Email/password sign-in) and ticket 02 (Session restore) — this is the data those two calls return, not a separate network round trip.
- Every other page's tickets that mention a capability/permission gate depend on this feature for how that gate is populated; see Architecture.md: Auth and Authorization/scoping for how `Capabilities` combines with `Access.Check`'s per-project rank (`owner`/`editor`/`viewer`) to form the full authorization decision.

### Notes / edge cases
- `capabilities` and `roles` are recomputed on every token authentication (subject to the 30-second in-memory cache noted in ticket 02), not stored as a snapshot on the token — granting or revoking a `UserRole`/`RoleGrant` takes effect for an already-signed-in user within that cache window, with no need to sign out and back in.
- `"*"` is a magic string, not a real row in `RoleGrant.Capability` seeded for every admin — whichever role is granted `Capability = '*'` becomes a full admin role; a developer adding a new "almost everything" role must not assume `*` is reserved/validated anywhere beyond this string comparison.
- Because `Capabilities` is `DISTINCT`-aggregated across all of a user's roles combined, there is no way from the `Me`/`CurrentUser` shape alone to tell *which* role granted a given capability — only that the user has it.
- `UserRole.GrantedBy` (nullable FK to `AppUser`) and `GrantedAt` are stored at grant time but are not surfaced anywhere in this feature's response — they exist in the schema for audit purposes only, unused by the sign-in/session flow.
