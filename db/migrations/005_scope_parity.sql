-- Gridline v2 · migration 005 · scope match parity with today's scope leveling, qualifications,
-- and the tables for options, savings ideas, provisional sums and unit rates (built now, switched off).
SET search_path = gridline, public;

-- bidder columns: hide a lane on scope match (was: hide subcontractor); lane order is LaneNo
ALTER TABLE Bid ADD COLUMN IF NOT EXISTS HiddenInScope boolean NOT NULL DEFAULT false;

-- qualifications (was: clarifications) per trade or for the whole version, and reusable sets (was: clarification templates)
CREATE TABLE IF NOT EXISTS Qualification (
  QualificationId bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  VersionId     bigint NOT NULL REFERENCES ProjectVersion ON DELETE CASCADE,
  TradeId       bigint REFERENCES Trade ON DELETE CASCADE,        -- null = applies to the whole project
  Kind          text NOT NULL CHECK (Kind IN ('assumption','exclusion','qualification')),
  SortKey       numeric(12,4) NOT NULL,
  QualText      text NOT NULL,
  ShowToClient  boolean NOT NULL DEFAULT true,
  ChangedAt     timestamptz NOT NULL DEFAULT now(),
  ChangedBy     bigint REFERENCES AppUser
);
CREATE INDEX IF NOT EXISTS IxQualificationTrade ON Qualification (VersionId, TradeId, SortKey);
CREATE TABLE IF NOT EXISTS QualificationSet (
  QualificationSetId bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  SetName       text NOT NULL UNIQUE,
  CodeId        int REFERENCES WorkCode,
  IsActive      boolean NOT NULL DEFAULT true
);
CREATE TABLE IF NOT EXISTS QualificationSetLine (
  QualificationSetLineId bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  QualificationSetId bigint NOT NULL REFERENCES QualificationSet ON DELETE CASCADE,
  Kind          text NOT NULL CHECK (Kind IN ('assumption','exclusion','qualification')),
  SortOrder     smallint NOT NULL,
  QualText      text NOT NULL
);

-- options (was: alternates) and savings ideas (was: value engineering)
CREATE TABLE IF NOT EXISTS PriceOption (
  OptionId      bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  VersionId     bigint NOT NULL REFERENCES ProjectVersion ON DELETE CASCADE,
  Kind          text NOT NULL CHECK (Kind IN ('option','saving')),
  OptionNo      text NOT NULL,                                   -- ALT-01, SI-01
  Description   text NOT NULL,
  Status        text NOT NULL DEFAULT 'proposed' CHECK (Status IN ('proposed','accepted','rejected')),
  SortKey       numeric(12,4) NOT NULL,
  ChangedAt     timestamptz NOT NULL DEFAULT now(),
  UNIQUE (VersionId, OptionNo)
);
CREATE TABLE IF NOT EXISTS PriceOptionTrade (                     -- which trades an option touches, and the amount carried
  OptionId      bigint NOT NULL REFERENCES PriceOption ON DELETE CASCADE,
  TradeId       bigint NOT NULL REFERENCES Trade ON DELETE CASCADE,
  CarriedAmount numeric(16,2),
  PRIMARY KEY (OptionId, TradeId)
);
CREATE TABLE IF NOT EXISTS PriceOptionBid (                       -- each bidder's price for the option (scope match lane cell)
  OptionId      bigint NOT NULL REFERENCES PriceOption ON DELETE CASCADE,
  BidId         bigint NOT NULL REFERENCES Bid ON DELETE CASCADE,
  Amount        numeric(16,2),
  AnswerText    text,
  PRIMARY KEY (OptionId, BidId)
);

-- provisional sums (was: allowances)
CREATE TABLE IF NOT EXISTS ProvisionalSum (
  SumId         bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  VersionId     bigint NOT NULL REFERENCES ProjectVersion ON DELETE CASCADE,
  TradeId       bigint REFERENCES Trade ON DELETE CASCADE,
  Kind          text NOT NULL CHECK (Kind IN ('ownerprescribed','miscellaneous','tradeentered')),
  Description   text NOT NULL,
  Amount        numeric(16,2) NOT NULL DEFAULT 0,
  SortKey       numeric(12,4) NOT NULL,
  ChangedAt     timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS ProvisionalSumBid (
  SumId         bigint NOT NULL REFERENCES ProvisionalSum ON DELETE CASCADE,
  BidId         bigint NOT NULL REFERENCES Bid ON DELETE CASCADE,
  Kind          AnswerKind NOT NULL DEFAULT 'blank',
  Amount        numeric(16,2),
  PRIMARY KEY (SumId, BidId)
);

-- unit rates (was: unit prices)
CREATE TABLE IF NOT EXISTS UnitRate (
  UnitRateId    bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  TradeId       bigint NOT NULL REFERENCES Trade ON DELETE CASCADE,
  ItemText      text NOT NULL,
  UomCode       text REFERENCES UnitOfMeasure,
  Qty           numeric(14,4),
  SortKey       numeric(12,4) NOT NULL,
  ChangedAt     timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS UnitRateBid (
  UnitRateId    bigint NOT NULL REFERENCES UnitRate ON DELETE CASCADE,
  BidId         bigint NOT NULL REFERENCES Bid ON DELETE CASCADE,
  Rate          numeric(14,4),
  PRIMARY KEY (UnitRateId, BidId)
);

-- feature switches (off = hidden in the app, API answers 404)
INSERT INTO Setting (SettingKey, SettingName, Note, SettingValue) VALUES
 ('feature.options', 'Options & savings ideas', 'was Alternates & Value engineering', 'false'),
 ('feature.provisionalsums', 'Provisional sums', 'was Allowances', 'false'),
 ('feature.unitrates', 'Unit rates', 'was Unit prices', 'false')
ON CONFLICT (SettingKey) DO NOTHING;

-- a starter qualification set
INSERT INTO QualificationSet (SetName) VALUES ('Standard trade qualifications') ON CONFLICT (SetName) DO NOTHING;
INSERT INTO QualificationSetLine (QualificationSetId, Kind, SortOrder, QualText)
SELECT s.QualificationSetId, x.k, x.o, x.t FROM QualificationSet s,
 (VALUES ('assumption', 1, 'Normal working hours, Monday to Friday.'), ('assumption', 2, 'Pricing is valid for 60 days.'),
         ('exclusion', 3, 'Hazardous material abatement.'), ('exclusion', 4, 'Permits and fees unless stated.'),
         ('qualification', 5, 'Based on drawings and specifications listed in the bid package.')) x(k, o, t)
WHERE s.SetName = 'Standard trade qualifications' AND NOT EXISTS (SELECT 1 FROM QualificationSetLine l WHERE l.QualificationSetId = s.QualificationSetId);

INSERT INTO SchemaChange (ChangeNo, ChangeName) VALUES (5, 'Scope match parity, qualifications; options, provisional sums, unit rates (switched off)') ON CONFLICT (ChangeNo) DO NOTHING;
