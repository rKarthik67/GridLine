## Title
Bid portal — Submit bid response

### Summary
The sub enters a base bid amount, free-text answers against each scope line, and general notes/qualifications, then submits the whole form in one request. The submission is stored, the invite is locked to "submitted", and the GC's project team is notified in real time (on any internal tab open to that project) that a bid came in.

### User story
As an invited subcontractor, I want to submit my base bid, per-line scope answers, and notes in one step so that the GC's estimator can review and carry my numbers without me having to email anything.

### Acceptance criteria
- [ ] The "Submit bid" button is disabled while the `base` input is empty (`disabled={!base}` — a plain truthy-string check, not a numeric/positive check); it is otherwise always clickable.
- [ ] On click, the base amount is parsed client-side: `Number(base.replace(/[$,]/g, '')) || null` (strips `$` and `,` only; any other non-numeric text parses to `null`, not an error).
- [ ] Each scope line's answer is free text, tracked per `lineId` in local state; blank/whitespace-only answers are dropped before sending.
- [ ] Request: `POST /api/portal/{token}` with body `{ baseAmount, notes, answers: [{ lineId, value }] }`.
- [ ] Server rejects with `400` ("Enter your base bid.") when `baseAmount` is `null` or `<= 0`.
- [ ] Server rejects with `409` (code `duplicate`, "This bid was already submitted. Contact the estimator to change it.") if the invite's `Status` is already `submitted`.
- [ ] Server re-validates every submitted answer's `lineId` against the trade's current non-archived, non-heading `ScopeLine` rows; any `lineId` not in that set, or with a blank `value`, is silently dropped (no error returned for junk/stale line IDs).
- [ ] Each surviving answer is parsed server-side (`Rules.ParseAnswer`) into a normalized `{ lineId, kind, amount, text }`: recognized "included"/"excluded" phrasing, the literal word `price`, a leading `=` arithmetic expression (digits/`+-*/()` only) evaluated to a rounded 2-decimal amount, a plain number or parenthesized number (parsed as a negative amount), otherwise stored verbatim as free text.
- [ ] On success the server returns `200 { submissionId, received }`; the frontend sets local `done = true`, which immediately swaps the form for the same "Thank you" card used when the invite is already submitted.
- [ ] Any thrown error (400, 409, or otherwise) is caught and its message shown inline in red (`role="alert"`) below the form; the form stays interactive and `done` stays `false` so the sub can correct and retry.
- [ ] A successful submit permanently flips `BidInvite.Status` to `submitted` and stamps `SubmittedAt` — there is no self-service edit/resubmit after this; the sub must contact the estimator (see Notes).

### Technical spec
**Frontend:** `web/src/pages/Portal.tsx`, `submit()` handler. Calls `api.post(`/portal/${token}`, { baseAmount, notes, answers })` (no explicit response type parameter — a loose/`unknown`-typed call). Catches `ApiError`/`Error` and renders `(e as Error).message` as the inline error text. No TanStack Query mutation/cache invalidation is used — just local `useState` (`done`, `err`).

**Backend:** `POST /portal/{token}` in `Endpoints/PortalEndpoints.cs`, `.AllowAnonymous()`. Unlike almost every other mutating endpoint in the codebase, this one does **not** go through `Work.Begin`/`Work.Commit` — it opens its own connection + `BeginTransactionAsync` directly and manages the commit and SignalR broadcast by hand (there is no `CurrentUser`/capability/`Access.*` check since the caller isn't authenticated; the token is the only authorization). Logic, in order:
1. `Find(c, token, t)` re-resolves the invite the same way as the `GET` (see `01-view-bid-request.md`); `inv.Status is "submitted"` → `409 duplicate`.
2. `body.BaseAmount is null or <= 0` → `400`.
3. Loads the trade's current valid line IDs: `SELECT LineId FROM ScopeLine WHERE TradeId = @t AND ArchivedAt IS NULL AND NOT IsHeading`.
4. Filters `body.Answers` to those IDs with a non-blank `Value`, maps each through `Rules.ParseAnswer(a.Value)` (`Calc/Rules.cs`) into `{ lineId, kind, amount, text }`.
5. `INSERT INTO BidSubmission (InviteId, BaseAmount, Answers, Notes) VALUES (@i, @b, @a::jsonb, @n) RETURNING SubmissionId`.
6. `UPDATE BidInvite SET Status = 'submitted', SubmittedAt = now() WHERE InviteId = @id`.
7. `INSERT INTO ActivityEvent (ProjectId, VersionId, Verb, SubjectType, SubjectKey, AfterState) VALUES (@p, @v, 'portal.submit', 'submission', @sid, @a::jsonb)` — written directly (not via `Work.Log`), so `UserId` is left `NULL` (no current user to attribute it to).
8. Commits the transaction, then manually sends `hub.Clients.Group(ProjectHub.Group(inv.ProjectId)).SendAsync("changed", new ChangeEvent(ev, "portal.submit", "submission", sid.ToString(), 0, inv.CompanyName, inv.VersionId, inv.ProjectId))` — `UserId` in the `ChangeEvent` is hardcoded `0` and `PersonName` is the sub's `TradePartner.CompanyName`, so any internal tab open on that project sees a "`{companyName} submitted an online bid`" toast (verb text from `web/src/app/verbs.ts`: `'portal.submit': 'submitted an online bid'`) and invalidates its cached data — this is the only way the internal app learns a submission happened in real time, since there's no requesting tab to exclude (no `X-Tab-Id` on an anonymous public page).
9. Returns `200 { submissionId, received: DateTime.UtcNow }`.

**Database:** `BidSubmission` (`001_init.sql`): `SubmissionId` identity PK, `InviteId bigint NOT NULL REFERENCES BidInvite ON DELETE CASCADE`, `BaseAmount numeric(16,2)`, `Answers jsonb NOT NULL DEFAULT '[]'` (shape `[{lineId, kind, amount, text}]`), `Notes text`, `SubmittedAt timestamptz NOT NULL DEFAULT now()`, `Decision ReviewDecision NOT NULL DEFAULT 'pending'` (`'pending'|'accepted'|'rejected'` — set later by the estimator, out of scope here), `DecidedBy`, `DecidedAt`. `BidInvite.Status`/`SubmittedAt` updated as above. `ActivityEvent` row inserted with `Verb = 'portal.submit'`, `SubjectType = 'submission'`, `SubjectKey = <SubmissionId>`. No trigger fires on any of these three tables for this write (`BidInvite`/`BidSubmission`/`ActivityEvent` are not in the `TouchRow`/`SetProjectId` trigger lists in `001_init.sql`).

### Dependencies
- Depends on `01-view-bid-request.md` (same page) — the line IDs validated here are exactly the set loaded by the `GET`.
- Feeds the Bid board page's submission review/decide feature (`GET /submissions/{id}` and `POST /submissions/{id}/decide}` in the same `PortalEndpoints.cs`, documented under the Bid board page, not this one) — decide applies the stored `Answers`/`BaseAmount` to `ScopeAnswer`/`Bid` and triggers `Recalc.TradeAndSummary`.
- See `_Overview/Architecture.md`: Realtime (`changed` SignalR event / "someone else just changed this" toast — here raised from an anonymous request that has no originating tab to exclude) and Unit of work (`Work`/`Access` pattern — this endpoint deliberately does not use it, see Notes) and Recalculation engine (downstream of acceptance, not of this endpoint itself).

### Notes / edge cases
- Because this endpoint hand-rolls its transaction instead of using `Work`, it also skips the standard `Access.*` scope check — that's intentional (there is no authenticated caller to scope-check), but it means none of the usual "locked version" (`423`) protections apply to a portal submission; a submission can land even if the project version is `locked`.
- There is no DB-level uniqueness/lock preventing two submissions on the same invite: the `Status is "submitted"` check is read-then-write, not enforced by a constraint, so two near-simultaneous submits (e.g. two browser tabs with the same link) could both pass the check before either commits and create two `BidSubmission` rows for one invite; downstream code (`Find` for invites, bid-board queries) always picks the latest via `ORDER BY SubmissionId DESC LIMIT 1`, silently discarding the earlier one.
- `BidInvite.Status = 'submitted'` is sticky, but the separate `POST /invites/{id}/resend` endpoint (same file, internal/authenticated) does not check current status before resetting `TokenHash`/`Status = 'sent'` — so an estimator can effectively un-lock a submitted invite and let the sub submit again on a new token, bypassing the `409 duplicate` guard entirely. There is no in-portal "resubmit" UI; this is strictly an estimator-initiated workaround.
- Client-side `disabled={!base}` is the only gate on the button; a sub can type `"abc"` into Base bid (making the button enabled) and the actual parsed `baseAmount` sent will be `null`, which the server then rejects with its own 400 — so the visible button state does not guarantee a valid submission.
- The `notes` field has no length limit or sanitization in the frontend or endpoint; it's stored as-is in `BidSubmission.Notes`.
