## Title
Bid board — Add a bidder to a trade

### Summary
From a trade's row (either the "+ Invite" tile at the end of its bid lanes, or the row's kebab menu), the estimator adds another bidder/lane with a company name, optional contact email, and an optional starting base bid, matching against the trade partner directory as they type.

### User story
As an estimator, I want to add a new bidder to a trade — matched against our trade partner directory, or created on the fly — so that I can get another price into the comparison without leaving the bid board.

### Acceptance criteria
- [ ] The "+ Invite" tile (shown only to editors, in the first empty bid column) and the row kebab menu's "Add bidder" both open the same modal for that trade.
- [ ] Typing a company name (2+ characters) searches the trade partner directory (`GET /partners?search=`) and shows up to 50 matches with company name, contact, and email; selecting one locks in its `partnerId` and pre-fills the email.
- [ ] The Add button is disabled until a name is entered; it is labeled "New companies are added to the directory (marked *)" when the typed name doesn't match a selected partner, vs. "From the trade partner directory" when it does.
- [ ] Submitting without `Base bid` leaves the new bid in state `invited` (not posted); submitting with a base bid posts it immediately (state `posted`, unless caller-specified otherwise).
- [ ] Adding a bidder that is already bidding on the trade (same `partnerId`, not archived) is rejected with a 409 ("{name} is already bidding on this trade.").
- [ ] On success, the new bid appears in the grid (new lane, numbered after the current max lane), the modal closes, and a toast "{name} added" is shown.

### Technical spec
**Frontend:** `web/src/pages/project/BidBoard.tsx`, exported component `AddBidModal` (`setAddBidFor(t)`). Directory search: `useQuery({ queryKey: ['partners', name], queryFn: () => api.get<{ partnerId; companyName; email; contact }[]>('/partners?search=${encodeURIComponent(name)}') , enabled: name.length >= 2 && partnerId == null })`. Submit: `api.post<TradeReply>('/trades/${t.tradeId}/bids', { bidderName, partnerId, email, baseAmount })`; the reply flows through the page's `after(r)` to merge the updated trade and invalidate `price-roll-up`.
**Backend:** `GET /partners` in `Endpoints/AuthEndpoints.cs` — anonymous-capability lookup (no `Access` scope check; it's directory-wide, not project-scoped), returns up to 50 `TradePartner` rows matching `CompanyName ILIKE '%search%'`, each with its primary `PartnerPerson` email/contact. `POST /trades/{id:long}/bids` in `Endpoints/TradeEndpoints.cs`: `Access.Trade(w, id)` (editor scope), delegates to the shared `TradeEndpoints.AddBid(w, id, body, "manual")` helper, then `Recalc.TradeAndSummary(w, id, "bid.create")`, logs `bid.create` against subject `bid`, returns `201` with a `TradeReply`.
`AddBid` business logic (shared with the invite-link and Excel-import paths): resolves/creates the `TradePartner` by case-insensitive company name match (new ones get `AddedByUser = true`, which drives the "*" marker shown next to the bidder's name); resolves/creates a `PartnerPerson` contact by email or name when either is supplied; rejects a duplicate active bid for the same partner on the trade (409); computes the initial `State` (`invited` if no base amount, else `posted`, unless `State` is explicitly given) and validates it against the known `BidState` list; assigns the next `LaneNo` for the trade (`max(LaneNo) + 1` among lanes with `LaneNo > 0` — this excludes soft-deleted/removed bids, which carry negative lane numbers, not `HiddenInScope` lanes, which is an unrelated display-only flag used by Scope match).
**Database:** Inserts into `TradePartner` (`CompanyName`, `AddedByUser`) when no match exists — enforced unique by `UxPartnerName ON TradePartner (lower(CompanyName))`; inserts into `PartnerPerson` (`PartnerId`, `FullName`, `Email`, `IsPrimary`) when contact info is given; inserts into `Bid` (`TradeId`, `PartnerId`, `PersonId`, `BidderName`, `LaneNo`, `State`, `IsPosted`, `BaseAmount`, `BaseSource = 'manual'`, `ChangedBy`), unique on `(TradeId, LaneNo)`. `Bid.ProjectId` is filled automatically by trigger `TrgBidProject BEFORE INSERT OR UPDATE OF TradeId ON Bid` (function `SetProjectId()`), which denormalizes the project id from the parent `Trade` so downstream project-scoped queries don't need to join through `Trade`.

### Dependencies
Shares `TradeEndpoints.AddBid` with "Bid details drawer" (invite-link creation) and with the Excel import feature ("Import trades and bidders from Excel"), which inserts `Bid` rows directly rather than via this endpoint but follows the same partner-matching idea. Newly added bids immediately participate in "Pick or clear a trade's bid" once posted. See Architecture.md: Work unit-of-work, Recalculation engine.

### Notes / edge cases
- `GET /partners` has no `Access` check at all — any authenticated caller (any project) can search the full company directory regardless of project membership; it is intentionally company-wide reference data, not project-scoped.
- A bidder typed with a name that happens to case-insensitively match an existing `TradePartner.CompanyName` is silently attached to that existing partner (not marked "added by user") even if the user never selected it from the autocomplete list — the match is server-side, not just the UI's dropdown selection.
- The "already bidding" duplicate check is keyed on `(TradeId, PartnerId)`, not on bidder name, so a typo'd/retyped company name that still resolves to the same `TradePartner` row (e.g. same name different case) is blocked, while a deliberately different (but same real-world) partner name creates a second, unrelated partner row.
- `LaneNo` assignment only considers lanes with `LaneNo > 0`; removed bids are given negative lane numbers (see "Bid details drawer" — remove bid), so lane numbers are not reused and can exceed the count of live bids.
