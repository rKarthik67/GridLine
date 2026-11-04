# StartProject — ticket index

- `01-import-excel-upload-and-validate/` — Download the blank template and upload a filled `.xlsx` workbook to `POST /imports/check`, showing a validation preview (issues, trade list, partner matches) before anything is created.
- `02-import-excel-create-project/` — Confirm a checked import via `POST /imports/{token}/create`, turning the staged workbook into a fully populated project (trades, bidders, zones, crew, scope) in one transaction and navigating into it.
- `03-start-blank-project/` — Fill the short blank-start form and create an empty project + version 1 via `POST /projects`, ready for manual data entry.
