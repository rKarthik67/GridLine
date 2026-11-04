# Price roll-up — ticket index

- `01-view-the-price-roll-up/` — Loads and renders the build-up strip, zone table, trade drill-in rows, heat map and view toggles for the price roll-up.
- `02-edit-markup-tax-rate-inline/` — Inline-edits a markup/tax line's rate percentage, triggering instant client recalculation and a server save + authoritative recalc.
- `03-manage-custom-indirect-items/` — Adds, inline-edits (name/amount/outside-fee), and deletes custom indirect cost lines on the roll-up.
- `04-export-price-roll-up/` — Downloads the current roll-up as a server-generated Excel workbook or PDF via the page's Export menu.
- `05-estimator-what-if-modal/` — Opens a sandbox "Estimator" modal that recomputes the whole build-up client-side from edited inputs, with a buyout/shared-savings calculator and CSV export, saving nothing.
