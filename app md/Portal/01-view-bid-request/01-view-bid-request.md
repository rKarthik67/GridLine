## Title
Bid portal — View bid request

### Summary
An invited subcontractor opens a private, token-gated link (`/bid/:token`, no sign-in) and sees the project name, trade name, the GC's company name, the bid due date, an optional message from the estimator, and the trade's scope lines to review before answering. This is the sub's only entry point into Gridline — there is no account, no password, just the link.

### User story
As an invited subcontractor, I want to open my private bid link and see the scope I'm being asked to price so that I can review the requirements before entering my numbers.

### Acceptance criteria
- [ ] Route `/bid/:token` is public (no `Private` auth guard) and renders `Portal.tsx`; token comes from the URL param.
- [ ] On mount, a TanStack Query with key `['portal', token]` calls `GET /api/portal/{token}`; while it is in flight the page shows "Loading your bid form…".
- [ ] If the query errors (token not found, expired, or invite status is `withdrawn`/`expired`/`declined`, or the trade is archived) the page shows "This bid link is not valid or has expired." — no distinction between "bad token" and "expired" is shown to the sub.
- [ ] If the invite's returned `status` is `submitted`, the page shows a "Thank you" card ("Your bid for {tradeName} on {projectName} has been received.") instead of the form, even on a fresh page load (not just after the sub's own submit in this session).
- [ ] Otherwise the page renders: an eyebrow with `companyName` (the sub's own company, from `TradePartner.CompanyName` — not the GC's), an `<h1>` with `tradeName`, and a line with `projectName` plus `· due {dueOn}` when `dueOn` is non-null.
- [ ] If `message` is non-null/non-empty, it is shown in its own card above the scope table (the estimator's free-text note entered when the invite was sent).
- [ ] The scope table has one row per returned line: heading rows (`isHeading === true`) render as a bold line spanning all 3 columns with no inputs; normal rows render `lineText`, `desired`, and an empty-by-default text input for the sub's answer (placeholder "Included / Excluded / $ amount").
- [ ] Lines are returned in the order: all `baserequirement` section lines first, then `tradeitem` lines, each ordered by `SortKey`; lines in the `zone` section are never included on the portal.
- [ ] Only non-archived scope lines for the invited trade are returned (`ArchivedAt IS NULL`).
- [ ] The first time the invite is opened (`Status = 'sent'`), the server flips it to `opened` and stamps `OpenedAt`; the response always reports `status` as `"opened"` when the stored status is `"sent"` (so the sub never sees a raw `"sent"` state).

### Technical spec
**Frontend:** `web/src/pages/Portal.tsx`, default export `Portal`. Reads `token` via `useParams()`. `useQuery({ queryKey: ['portal', token], queryFn: () => api.get<PortalData>(`/portal/${token}`) })`. `PortalData` (declared inline in `Portal.tsx`) is `{ projectName, tradeName, companyName, dueOn, message, status, lines: { lineId, section, isHeading, lineText, desired }[] }` — note the endpoint also returns `csiCode` and `company` (the GC's `CompanyProfile.DisplayName`) and each line's `indent`, none of which `PortalData`/the JSX actually reads. No SignalR connection is made on this page (it is anonymous and outside `ProjectLayout`/`hub.ts`'s `join()`).

**Backend:** `GET /portal/{token}` in `Endpoints/PortalEndpoints.cs`, `.AllowAnonymous()` (no `Me()`/capability/`Access.*` check — the token itself is the authorization). Looks the invite up via the private `Find(c, token)` helper:
```sql
SELECT i.InviteId, i.TradeId, tr.ProjectId, tr.VersionId, i.Status::text AS Status, p.ProjectName, tr.TradeName, tr.CsiCode, tp.CompanyName,
       coalesce(tr.BidDueOn, p.BidDueOn) AS BidDueOn, i.Message, (SELECT DisplayName FROM CompanyProfile LIMIT 1) AS Company
FROM BidInvite i JOIN Trade tr ON tr.TradeId = i.TradeId JOIN Project p ON p.ProjectId = tr.ProjectId JOIN TradePartner tp ON tp.PartnerId = i.PartnerId
WHERE i.TokenHash = @h AND i.ExpiresAt > now() AND i.Status NOT IN ('withdrawn','expired','declined') AND tr.ArchivedAt IS NULL
```
(`@h = Tokens.Hash(token)`, SHA-256 — the plaintext token is never stored, same hashing helper as login tokens but a separate, unrelated secret). Not found → `404 ApiException.NotFound("This bid link")`, which surfaces as the frontend's generic invalid-link message. If `inv.Status == "sent"`, runs `UPDATE BidInvite SET Status = 'opened', OpenedAt = now() WHERE InviteId = @id` as a bare `ExecuteAsync` (no transaction, no `Work`, no `ActivityEvent`, no SignalR broadcast). Then queries scope lines:
```sql
SELECT LineId AS "lineId", Section::text AS "section", IsHeading AS "isHeading", LineText AS "lineText", Desired::text AS "desired", Indent AS "indent"
FROM ScopeLine WHERE TradeId = @t AND ArchivedAt IS NULL AND Section <> 'zone'
ORDER BY CASE Section WHEN 'baserequirement' THEN 1 ELSE 2 END, SortKey
```
Returns `{ inv.ProjectName, inv.TradeName, inv.CsiCode, inv.CompanyName, dueOn: inv.BidDueOn, inv.Message, status: inv.Status == "sent" ? "opened" : inv.Status, lines, company: inv.Company }`.

**Database:** `BidInvite` (`db/migrations/001_init.sql`): `TokenHash bytea NOT NULL UNIQUE` (the lookup index), `ExpiresAt timestamptz NOT NULL`, `Status InviteStatus` enum (`'draft','sent','opened','submitted','declined','expired','withdrawn'`), `OpenedAt timestamptz`. `Trade`: `ProjectId`, `VersionId`, `CsiCode`, `TradeName`, `BidDueOn`, `ArchivedAt` is not a real column on `Trade` in this migration set used here only as a WHERE filter — confirmed present as `tr.ArchivedAt IS NULL` in the query (Trade carries an `ArchivedAt` column). `ScopeLine`: `TradeId`, `Section` (`LineSection` enum: `'baserequirement','tradeitem','zone'`), `IsHeading`, `LineText`, `Desired` (`DesiredKind` enum: `'included','excluded','price','text'`), `Indent`, `SortKey numeric(12,4)`, `ArchivedAt`; indexed by `IxScopeLineTrade ON ScopeLine (TradeId, Section, SortKey) WHERE ArchivedAt IS NULL`, which this query's filter/sort matches directly. `TradePartner.CompanyName`. `CompanyProfile.DisplayName` (single-row company settings table, `LIMIT 1`).

### Dependencies
- Depends on the Bid board feature that creates the invite (`POST /trades/{id}/invites` in `PortalEndpoints.cs`, documented under the Bid board page) — that is what generates the plaintext token, stores its hash, and sets `ExpiresAt`; this ticket only consumes an already-sent invite.
- Feeds `02-submit-bid-response.md` (same page): the lines/line IDs loaded here are what the submit POST validates answers against.
- See `_Overview/Architecture.md`: Auth (token hashing pattern, reused here for an unrelated anonymous-access secret, not the login token) and Database conventions (`ScopeLine` index).

### Notes / edge cases
- The `sent → opened` transition is a side effect of a `GET` request and bypasses the platform's normal `Work` unit-of-work pattern entirely: no `ActivityEvent` row is logged and no SignalR `changed` event fires, so a GC estimator watching the Bid board tab will not see "opened" live — they only see it next time they reload/query invites.
- Reopening the link after it is already `opened` does not re-stamp `OpenedAt` or otherwise change state (the `if (inv.Status == "sent")` guard only fires once).
- The invalid-link and expired-link cases are indistinguishable to the sub by design — the API returns the same `404` for a wrong token, a revoked/withdrawn invite, and an expired invite.
- Nothing about this feature saves partial progress: if the sub fills in the form and closes the tab without pressing Submit, no server state changes at all (the only server write in this ticket is the `opened` status flip).
