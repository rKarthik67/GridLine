## Title
Scope match — Files tab (trade documents: drawings, proposals, bid forms)

### Summary
A per-trade file repository — upload drawings, proposal PDFs, or bid forms, optionally tagging a file to a specific bidder, then list, download, or delete them. Used alongside the Bid inbox's proposal reader (ticket 11) for keeping a trade's paperwork in one place.

### User story
As an estimator, I want to upload and organize files for a trade (optionally per bidder) and download or delete them later, so that drawings, proposals and bid forms stay attached to the right trade instead of scattered in email.

### Acceptance criteria
- [ ] A drag-and-drop/browse upload zone (`canEdit` only) accepts multiple files at once; a dropdown above it lets the user attach the upload to "Whole trade" or a specific bidder (lane letter + name) before dropping/browsing.
- [ ] While uploading, the drop zone shows "Uploading…"; on success, a toast reads "{n} file(s) uploaded" and the file list refetches.
- [ ] The file list table shows, per file: an icon + file name, the trade partner it's tagged to (or "whole trade" if untagged), a `docKind` tag, size in whole KB (rounded, minimum 1 KB shown), and "{uploaded date}{· uploaded-by name if known}".
- [ ] An empty trade shows "No files on this trade yet."
- [ ] Every row has a Download button (streams the original file with its original name) and, when `canEdit`, a Delete button (toast "File deleted" on success, list refetches).
- [ ] Uploading a file over 50 MB is rejected per-file with "{fileName} is larger than 50 MB." without uploading any of the batch's other files that were already processed before the oversized one was hit.
- [ ] Choosing to upload with zero files selected/dropped is rejected ("Choose at least one file.").
- [ ] A `.pdf` file uploaded **with** a bidder selected is auto-classified `docKind = 'proposal'`; every other combination (no bidder selected, or a non-PDF) is classified `'other'`. There is no manual kind picker on this tab (unlike the project-level Documents list elsewhere in the app).

### Technical spec
**Frontend:** `web/src/pages/project/ScopeMatch.tsx` — `FilesTab({ tradeId, vid, canEdit, bids })`. Data: `useQuery(['trade-files', vid, tradeId], () => api.get<{documentId, docKind, fileName, byteSize, bidId, uploadedAt, uploadedBy}[]>('/trades/{tradeId}/documents'))`. Upload: builds a `FormData` with one or more `file` entries and an optional `bidId` field, `api.post('/trades/{tradeId}/documents', fd)`. Download: `api.download('/documents/{documentId}/file', fileName)`. Delete: `api.del('/documents/{documentId}')`.
**Backend:** `GET /trades/{id:long}/documents` (`ScopeExtrasEndpoints`) — `Access.Trade` (viewer+); joins `Document` to `AppUser` for the uploader's display name, ordered newest first. `POST /trades/{id:long}/documents` (`ScopeExtrasEndpoints`, `.DisableAntiforgery()`) — `Access.Trade` (editor+); reads the multipart form, parses an optional `bidId`; per file, rejects >50 MB, saves it via `Storage.Save` under `documents/{projectId}` keyed by a new GUID + original extension, inserts one `Document` row per file (`DocKind` computed by the `.pdf` + `bidId` rule above); logs `document.upload` with the file count; requires at least one file. `GET /documents/{id:long}/file` (`DocumentEndpoints`, shared across the app) — looks up the document by id (404 if missing), checks `Access.Project(..., "viewer")` using the document's own `ProjectId`, streams the file from storage with its original content type and name. `DELETE /documents/{id:long}` (`DocumentEndpoints`, shared) — looks up `ProjectId` from the document, `Access.Project` (editor+), **hard-deletes** the `Document` row (`DELETE FROM Document`) while leaving the underlying file on disk for audit purposes; logs `document.delete`.
**Database:** `Document` (`DocumentId` PK, `PublicKey uuid UNIQUE`, `ProjectId` FK→`Project` cascade, `TradeId`/`BidId` FKs added via `ALTER TABLE` after `Trade`/`Bid` exist (`FkDocumentTrade`/`FkDocumentBid`, both `ON DELETE CASCADE`), `DocKind CHECK IN ('proposal','import','export','drawing','other')`, `FileName`, `ContentType`, `ByteSize`, `StoragePath`, `UploadedBy` FK→`AppUser`, `UploadedAt`). No `RowRev`/`TrgTouch` on `Document` — it's effectively append/delete-only, no in-place edits.

### Dependencies
- Shares the `Document` table and the project-level file endpoints (`GET/DELETE /documents/{id}`) with the project's general Documents list and with the proposal reader (ticket `11-bid-inbox-proposal-reader.md`), which also writes `Document` rows (kind `proposal`) when a proposal PDF is uploaded through the inbox instead of this tab.
- See `_Overview/Architecture.md`: Authorization/scoping (`Access.Trade`/`Access.Project`), Unit of work (upload/delete both log `ActivityEvent`s).

### Notes / edge cases
- Deleting a `Document` here only removes the database row — the physical file under `StoragePath` is deliberately left on disk ("file stays on disk (audit); the record is gone") — do not expect storage usage to shrink on delete, and do not rely on delete to purge sensitive content from disk.
- The same physical upload endpoint and 50 MB cap (`DocumentEndpoints.MaxUpload`) is shared with the project-level Documents uploader and the proposal-reader uploader (ticket 11); changing the limit in one spot does not change it everywhere unless all three call sites are updated together (they currently share the same constant, but confirm before assuming a change is page-scoped).
- A file uploaded here tagged to a bidder and ending in `.pdf` becomes indistinguishable in storage/classification from a proposal uploaded through the Bid inbox's dedicated "Upload proposal PDF" control (ticket 11) — but only the inbox's upload path creates a `ProposalReading` row and queues it for reading; uploading the same PDF here does **not** trigger the proposal reader.
- `DELETE /documents/{id}` requires **editor** access on the document's project, not the narrower trade-level check used elsewhere on this tab — a user who is an editor on the project but was scoped out at the trade level (if that distinction is ever introduced) would still be able to delete a trade's files via this route.
