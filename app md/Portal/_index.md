# Portal — ticket index

- `01-view-bid-request/` — The invited sub opens the public `/bid/:token` link and sees the project/trade/company header, due date, estimator message, and the trade's scope lines (via `GET /portal/{token}`), with invalid/expired-link and already-submitted states.
- `02-submit-bid-response/` — The sub enters a base bid, per-line scope answers, and notes, and submits them all in one request (`POST /portal/{token}`), which locks the invite and notifies the GC's open project tabs in real time.
