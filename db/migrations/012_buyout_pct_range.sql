-- Gridline v2 · migration 012 · Trade.BuyoutPct was numeric(6,3) (at most ±999.999 %).
-- Picking a bid more than ~11× the trade budget failed with "numeric field overflow".
-- Widen it to numeric(19,2), the same size the original app uses for budgets (BidTrades.Budget decimal(19,2)).
-- There is no limit on how far over or under budget a trade can be, as in the original.
SET search_path = gridline, public;
ALTER TABLE Trade ALTER COLUMN BuyoutPct TYPE numeric(19,2);
INSERT INTO SchemaChange (ChangeNo, ChangeName) VALUES (12, 'Trade.BuyoutPct numeric(19,2): no overflow on picks far above budget') ON CONFLICT (ChangeNo) DO NOTHING;
