-- Gridline v2 · migration 007 · fee carve-outs (was: fee exclusions), bid total options,
-- on/off switches for rates and indirect items, and indirect item descriptions.
SET search_path = gridline, public;

-- Fee carve-outs: which parts are left out of the fee base, and whether they apply at all
ALTER TABLE ProjectVersion
  ADD COLUMN IF NOT EXISTS ApplyFeeExcludes        boolean NOT NULL DEFAULT false,   -- "Apply fee exclusions?" Yes / No
  ADD COLUMN IF NOT EXISTS FeeExcludes             text[]  NOT NULL DEFAULT '{}',    -- genreq, precon, reserves, bond, cover
  ADD COLUMN IF NOT EXISTS ExcludeGenreqFromDirect boolean NOT NULL DEFAULT false,   -- was: Exclude General Requirements From Direct Costs
  ADD COLUMN IF NOT EXISTS ReservePrecon           text[]  NOT NULL DEFAULT '{}';    -- reserves whose base also takes precon services

DO $$ BEGIN
  ALTER TABLE ProjectVersion ADD CONSTRAINT VersionFeeExcludesCheck
    CHECK (FeeExcludes <@ ARRAY['genreq', 'precon', 'reserves', 'bond', 'cover']::text[]);
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
  ALTER TABLE ProjectVersion ADD CONSTRAINT VersionReservePreconCheck
    CHECK (ReservePrecon <@ ARRAY['reserveproject', 'reservedesign', 'reserveescalation']::text[]);
EXCEPTION WHEN duplicate_object THEN NULL; END $$;

-- Rates page: a rate can be switched off (counts as 0) without losing the rate typed in
ALTER TABLE VersionMarkup ADD COLUMN IF NOT EXISTS IsOff boolean NOT NULL DEFAULT false;

-- Indirect items (was: user-defined indirect line items): description, and an on/off switch
ALTER TABLE IndirectItem
  ADD COLUMN IF NOT EXISTS Description text,
  ADD COLUMN IF NOT EXISTS IsOff boolean NOT NULL DEFAULT false;

INSERT INTO SchemaChange (ChangeNo, ChangeName) VALUES (7, 'Fee carve-outs, bid total options, rate and indirect item on/off switches') ON CONFLICT (ChangeNo) DO NOTHING;
