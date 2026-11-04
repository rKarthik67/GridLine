# Project profile (pageKey=profile) — ticket index

- `01-view-and-edit-project-details/` — View and edit the project's core details (name, client, stage, address, estimator/manager) on the Details tab, plus archive the project.
- `02-manage-zones/` — Add, reorder, edit and delete a version's breakout zones (area/unit/quantity, remainder, reserves) and version-wide area settings on the Zones tab.
- `03-manage-project-versions/` — List, rename, lock/unlock, mark primary, create and compare versions, and copy the whole project as a new one, on the Versions tab.
- `04-manage-rates-and-indirect-items/` — Edit markup rates (reserves, bond & cover, insurance, fee, sales tax), their on/off switches and fixed/budget amounts, and manage custom indirect cost items, on the Rates & indirects tab.
- `05-manage-fee-carve-outs/` — Choose which costs are carved out of the fee base, set the quoted fee %, configure reserve bases and bid total options, on the Fee carve-outs tab.
- `06-manage-project-members/` — View and (as an owner) manage who has viewer/editor/owner access to the project, on the Members tab.
- `07-upload-download-delete-documents/` — Upload, list, download and delete project documents (drawings, specs, bid forms), on the Documents tab.
- `08-view-activity-timeline/` — View a polling, read-only audit feed of every logged change on the project, on the Activity tab.
- `09-schedule-dates/` — Schedule section on Details: precon, construction, completion and closeout dates with week counts and salary escalation; preview + confirm via `PUT /projects/{id}/dates`; drives the Crew plan weeks.
