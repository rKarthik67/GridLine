-- Gridline v2 · migration 013 · alternates & value engineering, allowances and unit prices, switched on.
-- Same model as the original app (NCC / BidCalc):
--   * Alternates & VE (Alternates, AlternateTradeItems): one list per version; every trade enters its amount (+ note) per item;
--     Total = Σ trade amounts, Subtotal quoted = Total × Quoted %, each indirect = roll-up line × (Subtotal quoted ÷ Direct cost total)
--     when included, Total quoted = Subtotal quoted + indirects. Not part of the contract total.
--   * Allowances (Allowances, AllowanceTradeItems): owner prescribed (a scope line on every trade) or miscellaneous (one trade).
--     Each bidder prices the line in Scope match; a trade's contribution is the picked bid's amount; Total = Σ included contributions.
--     The scope lines count in the bids like any other line.
--   * Unit prices (UnitPrices, UnitPriceTradeItems): one list per version; every trade enters a price; one trade's price is selected;
--     Discounted price = price × Quoted %, indirects = discounted × each roll-up line's share of the contract, Quoted price = sum.
--     Not part of the contract total.
-- The tables from 005 were never used (the features were switched off), so the ones that don't fit are replaced.
SET search_path = gridline, public;

DROP TABLE IF EXISTS PriceOptionBid, ProvisionalSumBid, UnitRateBid;
DO $$ BEGIN   -- the 005 UnitRate (per trade, with ItemText) is replaced; a re-run leaves the new one alone
  IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'gridline' AND table_name = 'unitrate' AND column_name = 'itemtext') THEN
    DROP TABLE gridline.UnitRate CASCADE;
  END IF;
END $$;

-- ---------------------------------------------------------------- alternates (Kind 'option') & value engineering (Kind 'saving')
ALTER TABLE PriceOption DROP CONSTRAINT IF EXISTS priceoption_versionid_optionno_key;
ALTER TABLE PriceOption
  ADD COLUMN IF NOT EXISTS OptionIndex  int NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS QuotedPct    numeric(19,2) NOT NULL DEFAULT 100,
  ADD COLUMN IF NOT EXISTS Comments     text,
  ADD COLUMN IF NOT EXISTS IncludeCodes text[] NOT NULL DEFAULT '{genreq,precon,genconds,reserveproject,reservedesign,reserveescalation,bond,cover,liability,fee}',
  ADD COLUMN IF NOT EXISTS CreatedAt    timestamptz NOT NULL DEFAULT now(),
  ADD COLUMN IF NOT EXISTS ChangedBy    bigint REFERENCES AppUser,
  ADD COLUMN IF NOT EXISTS RowRev       int NOT NULL DEFAULT 1;
ALTER TABLE PriceOptionTrade ADD COLUMN IF NOT EXISTS Note text;
CREATE INDEX IF NOT EXISTS IxPriceOptionVersion ON PriceOption (VersionId);
CREATE INDEX IF NOT EXISTS IxPriceOptionTradeTrade ON PriceOptionTrade (TradeId);

-- ---------------------------------------------------------------- allowances
ALTER TABLE ProvisionalSum
  ADD COLUMN IF NOT EXISTS SumIndex  int NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS CreatedAt timestamptz NOT NULL DEFAULT now(),
  ADD COLUMN IF NOT EXISTS ChangedBy bigint REFERENCES AppUser,
  ADD COLUMN IF NOT EXISTS RowRev    int NOT NULL DEFAULT 1;
CREATE INDEX IF NOT EXISTS IxProvisionalSumVersion ON ProvisionalSum (VersionId);
CREATE TABLE IF NOT EXISTS ProvisionalSumTrade (                -- was AllowanceTradeItems: Included / Excluded per trade
  SumId     bigint NOT NULL REFERENCES ProvisionalSum ON DELETE CASCADE,
  TradeId   bigint NOT NULL REFERENCES Trade ON DELETE CASCADE,
  Included  boolean NOT NULL DEFAULT false,                       -- the original starts every trade as Excluded
  ChangedAt timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (SumId, TradeId)
);
ALTER TABLE ScopeLine ADD COLUMN IF NOT EXISTS SumId bigint REFERENCES ProvisionalSum ON DELETE SET NULL;   -- the allowance's line in a trade
CREATE INDEX IF NOT EXISTS IxScopeLineSum ON ScopeLine (SumId) WHERE SumId IS NOT NULL;
ALTER TABLE ScopeAnswer DROP CONSTRAINT IF EXISTS scopeanswer_source_check;   -- 'allowance' = filled in from the Allowances page
ALTER TABLE ScopeAnswer ADD CONSTRAINT scopeanswer_source_check CHECK (Source IN ('manual', 'import', 'portal', 'proposal', 'allowance'));

-- ---------------------------------------------------------------- unit prices
CREATE TABLE IF NOT EXISTS UnitRate (
  UnitRateId   bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  VersionId    bigint NOT NULL REFERENCES ProjectVersion ON DELETE CASCADE,
  UnitIndex    int NOT NULL DEFAULT 0,
  Description  text NOT NULL,
  Unit         text,
  QuotedPct    numeric(19,2) NOT NULL DEFAULT 100,
  Comments     text,
  IncludeCodes text[] NOT NULL DEFAULT '{precon,genconds,reserveproject,reservedesign,reserveescalation,bond,cover,liability,fee}',
  CreatedAt    timestamptz NOT NULL DEFAULT now(),
  ChangedAt    timestamptz NOT NULL DEFAULT now(),
  ChangedBy    bigint REFERENCES AppUser,
  RowRev       int NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS IxUnitRateVersion ON UnitRate (VersionId);
CREATE TABLE IF NOT EXISTS UnitRateTrade (                      -- was UnitPriceTradeItems
  UnitRateId bigint NOT NULL REFERENCES UnitRate ON DELETE CASCADE,
  TradeId    bigint NOT NULL REFERENCES Trade ON DELETE CASCADE,
  Price      numeric(19,4),
  Note       text,
  IsSelected boolean NOT NULL DEFAULT false,                       -- was Status = 'Selected'
  ChangedAt  timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (UnitRateId, TradeId)
);
CREATE UNIQUE INDEX IF NOT EXISTS UxUnitRateOneSelected ON UnitRateTrade (UnitRateId) WHERE IsSelected;

-- ---------------------------------------------------------------- switch the features on
UPDATE Setting SET SettingValue = 'true' WHERE SettingKey IN ('feature.options', 'feature.provisionalsums', 'feature.unitrates');

INSERT INTO SchemaChange (ChangeNo, ChangeName) VALUES (13, 'Alternates & VE, allowances and unit prices (as in the original app), switched on') ON CONFLICT (ChangeNo) DO NOTHING;
