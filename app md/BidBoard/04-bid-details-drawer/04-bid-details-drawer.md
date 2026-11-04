## Title
Bid board — Bid details drawer (edit, invite link, remove)

### Summary
Clicking a bidder's name on their tile opens a side drawer with that bid's full detail: base bid, status, trade partner name, contact, and totals vs. the lowest bid. From here the estimator can edit the bid's fields, generate (or re-share) an online bid invite link for the sub, pick/clear the bid, or remove it entirely.

### User story
As an estimator, I want to open a bid's details, edit its base amount/status/name, and generate an invite link so that I can correct or progress a single bid without leaving the bid board, and get the sub bidding online when needed.

### Acceptance criteria
- [ ] Clicking a bid's name (not the rest of the tile) opens the drawer for that bid without triggering the pick/unpick click handler.
- [ ] The drawer shows: total (or "Not posted"), base + scope-adjustment breakdown, "vs lowest" (percent over lowest, or "Lowest"), and status (or "Picked").
- [ ] Base bid is editable via an inline numeric `EditCell`; clearing it sends `clearBase: true` (nulls `BaseAmount`) rather than `baseAmount: 0`.
- [ ] Status is editable via a `<select>` of all nine `BidState` values (`invited, bidding, posted, late, incomplete, notplansspecs, undecided, disqualified, declined`); an unrecognised value is rejected with a 400.
- [ ] Trade partner name is editable inline; Contact (name · email) is read-only, sourced from the directory.
- [ ] All edit controls are disabled when `canEdit` is false.
- [ ] Entering a Base bid amount while the bid's state is `invited` or `bidding` automatically advances its state to `posted` server-side (unless the save also explicitly sets a state).
- [ ] The "Online bid link" panel (editors only) lets the user enter/confirm an email and click "Create link"; on success it shows the full URL (`location.origin + link`), a "Copy" button (clipboard, with a toast fallback if the clipboard API fails), and an "Email" `mailto:` link pre-filled with subject "Bid invitation · {tradeName}" and a body containing the link.
- [ ] Creating a link requires a valid email (must contain `@`); if the bid has no email and none is typed, the action fails with "Enter the sub's email address."
- [ ] "Pick this bid" / "Clear pick" appears only when `canEdit` and the bid `isPosted`; it calls the same pick/unpick behavior as the grid tile (see "Pick or clear a trade's bid").
- [ ] "Open in scope match" navigates to `../scope-match?trade={tradeId}` and closes the drawer.
- [ ] "Remove bid" (editors only, styled as destructive) soft-deletes the bid, closes the drawer, and updates the trade row; a toast reads "Bid removed".
- [ ] A stale edit (another tab changed the bid first) returns a 409 naming who changed it, same optimistic-concurrency pattern as elsewhere (uses the bid's `rowRev`).

### Technical spec
**Frontend:** `web/src/pages/project/BidBoard.tsx`, component `BidDrawer` (opened via `setDrawer({ t, b })`, re-resolved against the latest cached trade/bid on every render so it never shows stale data after a background refetch). Edits go through `patch(body, okMessage)` → `api.patch<TradeReply>('/bids/${b.bidId}', { ...body, rowRev: b.rowRev })`. Creating an invite link calls `api.post<{ link: string }>('/trades/${t.tradeId}/invites', { bidId: b.bidId, email })`. Removing calls `api.del<TradeReply>('/bids/${b.bidId}')`. All three go through `useAction().run`, and successful trade-affecting calls flow back through the page's `onReply`/`after` callback, which merges the trade into the `bid-board` cache and invalidates `price-roll-up`.
**Backend:**
- `PATCH /bids/{id:long}` in `Endpoints/TradeEndpoints.cs`: `Access.Bid(w, id)` (editor scope), `CheckRev(w, "Bid", "BidId", id, body.RowRev)`, validates `State` against the known list, updates `BidderName`/`BaseAmount`/`BaseSource`/`State` (auto-promoting `invited`/`bidding` → `posted` when a base amount is supplied and no explicit state is), then recomputes `IsPosted`/`IsCarried` (a disqualified/declined bid can never stay carried), calls `Recalc.TradeAndSummary(w, s.TradeId.Value, "bid.update", body.ClientHash)`, logs `bid.status` or `bid.update`, returns a `TradeReply` built from the bid's trade.
- `POST /trades/{id:long}/invites` in `Endpoints/PortalEndpoints.cs`: `w.User.Require("invite.send")`, `Access.Trade(w, id)`. If no `BidId` is given it creates one via `TradeEndpoints.AddBid` (source `"portal"`, state `invited`). Validates the resolved email (must contain `@`), generates a token (`Tokens.New()`), inserts a `BidInvite` row storing only the SHA-256 hash of the token, flips the bid from `invited` to `bidding`, logs `invite.send`, and returns `{ inviteId, bidId, token, link: "/bid/{token}", email, invites }` — the plaintext token only ever appears in this one response.
- `DELETE /bids/{id:long}`: `Access.Bid(w, id)`, soft-deletes (`ArchivedAt = now()`), un-picks it and reassigns its lane to a negative, non-colliding number (`LaneNo = -1 - (BidId % 30000)`) so the lane slot frees up, recalculates the trade/summary, logs `bid.delete`, returns a `TradeReply`.
**Database:** `Bid` columns touched: `BidderName`, `BaseAmount`, `BaseSource` ('manual' when a base amount is set here), `State` (enum `BidState`), `IsPosted`, `IsCarried`, `LaneNo`, `ArchivedAt`, `ChangedBy`, and `RowRev`/`ChangedAt` via `TrgTouch BEFORE UPDATE ON gridline.Bid`. `UxOneCarriedBid` (partial unique index on `Bid(TradeId) WHERE IsCarried AND ArchivedAt IS NULL`) still applies after a status change that would otherwise leave a disqualified bid carried. `BidInvite` (migration 001): `TradeId`, `BidId`, `PartnerId`, `SentToEmail citext`, `TokenHash bytea NOT NULL UNIQUE`, `Status InviteStatus`, `SentAt`/`OpenedAt`/`SubmittedAt`/`ExpiresAt`, `SentBy`; `IxInviteTrade ON BidInvite (TradeId)` (migration 003). Resolving/creating the bid's `PartnerPerson` contact reuses the same lookup logic as "Add a bidder to a trade" (`TradeEndpoints.AddBid`).

### Dependencies
Shares `TradeEndpoints.AddBid` with "Add a bidder to a trade" when the invite is created without an existing bid. Interacts with "Pick or clear a trade's bid" for the pick/clear action. The invite link opens the public bid portal (`pages/Portal.tsx` / `PortalEndpoints.cs`, documented separately under the Portal page) where the sub answers scope and submits a base amount feeding back into this same `Bid` row via `BidSubmission`. See Architecture.md: Work unit-of-work, Authorization/scoping.

### Notes / edge cases
- The invite token is stored only as a hash (`Tokens.Hash(token)`); if the response is missed or the link is lost, there is no way to recover it — a new invite (or `POST /invites/{id}/resend`) must be created instead.
- Creating an invite silently creates a new `Bid` (and possibly a new `TradePartner`) when `BidId` is omitted — the drawer's flow always has an existing bid, so this path is exercised from elsewhere (e.g. a from-scratch invite), not from this drawer directly.
- Changing a bid's `State` to `disqualified` or `declined` clears `IsCarried` as a side effect even though the request didn't touch carry status — a previously-picked bid silently stops being the trade's picked value and `carriedValue` may become unresolved.
- Setting `BaseAmount` via this drawer always stamps `BaseSource = 'manual'`, overwriting a prior `'import'`, `'portal'`, or `'proposal'` provenance marker shown elsewhere (e.g. export sheets) — there's no way to edit the amount without losing that provenance tag.
- `DELETE /bids/{id}` is a soft delete (`ArchivedAt`), not a hard delete; the row and its `BidInvite`/`ProposalReading` history remain in the database and are simply excluded by the `ArchivedAt IS NULL` filters used everywhere else.
