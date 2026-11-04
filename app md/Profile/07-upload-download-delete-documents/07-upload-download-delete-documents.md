## Title
Project profile — Upload, download and delete project documents

### Summary
The Documents tab lets an editor drag-and-drop or browse-upload files (drawings, specs, bid forms) onto the project, lists every uploaded document with its type, size and uploader, and lets any viewer download a file or an editor delete it. Files are stored on disk/blob storage with only metadata in the database.

### User story
As a project team member, I want to upload project documents and later find, download or remove them so that drawings, specs and bid forms are available to everyone working the project.

### Acceptance criteria
- [ ] Editors see a drop zone ("Drop files here or browse… — drawings, specs, bid forms (50 MB each)"); viewers do not see the drop zone at all.
- [ ] Both drag-and-drop and the file browse `<input>` support multiple files in one upload; all selected files are uploaded as kind `"other"` in one request.
- [ ] While an upload is in flight, "Uploading…" is shown below the drop zone.
- [ ] On successful upload, a "{n} file(s) uploaded" confirmation appears and the documents list is refetched.
- [ ] A file larger than 50 MB is rejected with "{fileName} is larger than 50 MB." and nothing is uploaded from that request; uploading with no files selected is rejected with "Choose at least one file."
- [ ] The document table lists file name, kind tag, size in KB, upload date, and uploader name (when known); it excludes any document of kind `"import"` (those belong to the Import feature, not this tab).
- [ ] An empty list shows "No documents yet." instead of an empty table.
- [ ] A Download button is available to any user who can at least view the project (works even for viewers) and streams the original file with its original filename.
- [ ] A Delete button is shown only to editors/owners; deleting removes the document from the list and shows a "Document deleted" confirmation. The underlying stored file is kept on disk for audit purposes even though the database record is gone.

### Technical spec
**Frontend:** `web/src/pages/project/Profile.tsx`, component `Documents({ project })`. Loads `useQuery({ queryKey: ['documents', project.projectId], queryFn: () => api.get<{...}[]>(\`/projects/${project.projectId}/documents\`) })`. Upload handler builds a `FormData` (`file` repeated per selected file, `kind: 'other'`) and calls `api.post(\`/projects/${project.projectId}/documents\`, fd)`. Download: `api.download(\`/documents/${d.documentId}/file\`, d.fileName)` (per Architecture.md, reads the filename from `Content-Disposition` when not explicitly supplied). Delete: `api.del(\`/documents/${d.documentId}\`)`. All editing gated by `canEdit = project.access !== 'viewer'`.

**Backend** (`DocumentEndpoints.cs`):
- `GET /api/projects/{id:long}/documents` — `Access.Project(c, null, ctx.Me(), id, "viewer")`; selects `DocumentId, DocKind, FileName, ContentType, ByteSize, TradeId, BidId, UploadedAt`, left-joined to `AppUser` for the uploader's display name, `WHERE d.ProjectId = @id AND d.DocKind <> 'import'`, ordered newest first.
- `POST /api/projects/{id:long}/documents` (multipart form, `.DisableAntiforgery()`) — `Access.Project(w, id)` (default `"editor"`). Reads `kind` from the form (defaults to `"other"`), rejects anything other than `drawing`/`other`/`proposal` with "Unknown document kind." Per uploaded file: rejects files over 50 MB with "{FileName} is larger than 50 MB."; saves the file via the injected `Storage` service under `documents/{projectId}/{guid}{ext}`, then inserts a `Document` row (`PublicKey` = the same guid, `ProjectId`, `DocKind`, `FileName`, `ContentType`, `ByteSize`, `StoragePath`, `UploadedBy`). If zero files ended up added, throws "Choose at least one file." Logs `"document.upload"` with the comma-joined new document ids as the subject key, commits, returns the list of new ids.
- `GET /api/documents/{id:long}/file` — looks up `ProjectId, StoragePath, FileName, ContentType` from `Document` (404 if missing), then `Access.Project(c, null, ctx.Me(), pid, "viewer")` — any project viewer can download. Streams the file from `Storage.Open(path)` with `Results.File(...)`.
- `DELETE /api/documents/{id:long}` — resolves the owning project id from `Document`, `Access.Project(w, pid)` (default `"editor"`). Deletes the `Document` row only — the comment explicitly notes "file stays on disk (audit); the record is gone." Logs `"document.delete"`.

**Database:** Table `Document` (`001_init.sql`): `DocumentId, PublicKey (uuid, unique), ProjectId (FK Project ON DELETE CASCADE), TradeId, BidId, DocKind (CHECK IN ('proposal','import','export','drawing','other')), FileName, ContentType, ByteSize, StoragePath, UploadedBy (FK AppUser), UploadedAt`. This tab only ever reads/writes rows with `DocKind IN ('drawing','other')` via its own endpoints (proposal uploads go through the separate `/bids/{id}/proposals` endpoint documented for Scope match / proposal reader, and `export`/`import` kinds are written by the Export/Import features). No FK constraint exists from `Document.TradeId`/`BidId` to their tables at the column level shown here (declared without `REFERENCES` inline — see the "FK below" comment in the migration), so those columns are simply nullable longs for this tab's own uploads.

### Dependencies
- See `_Overview/Architecture.md`: Authorization/scoping (`Access.Project` viewer vs. editor rank), Import/Export (shared `Document` table but different `DocKind` values and endpoints), background `ProposalReader` worker (also writes `Document` rows of kind `proposal`).
- Shares the `Document` table and the `/documents/{id}/file` download/delete endpoints with the proposal-upload flow documented under Scope match (`ScopeMatch.md`) — changing download/delete auth here affects that flow too.

### Notes / edge cases
- Deleting a document is a hard delete of the database row, not a soft delete like `Project.ArchivedAt` — but the physical file is intentionally left in storage, so storage usage is never reclaimed by this endpoint; any future "purge" job must reconcile orphaned files against the `Document` table itself (there is no tombstone row to look up afterward).
- The download endpoint checks only project-level `"viewer"` access, not per-document restrictions — once a document exists on a project, anyone who can open the project can download it, regardless of which tab (proposals, exports) originally created it.
- The upload endpoint applies the same single `kind` value to every file in a multi-file drop; you cannot upload a mixed batch of e.g. one drawing and one bid form with different kinds in a single request from this UI (the tab always sends `kind: 'other'` anyway).
- `ByteSize` displayed as KB in the UI (`/1024` with 0 decimal places) is read directly from the stored `ByteSize` column, which is whatever the `Storage.Save` call reported — if storage ever reports a different size than the actual file bytes (e.g. after transcoding), the UI will show the stale stored value.
