-- Gridline v2 · migration 006 · zones work like today's breakout areas.
--  * unit of area (SF/RSF/GSF/USF) and unit of quantity (N/A, Units, MW, Keys, Spaces) per zone
--  * "Remainder of base bid": one zone per version whose amount is the bid total minus the other zones
--  * "Exclude SF from project total" per zone, and "Zones equal total project area" per version
--  * reserve % per zone (was: project / design / escalation contingency per breakout area)
--  * the version's primary unit of area / quantity and total units (sum of zones using the primary unit)
SET search_path = gridline, public;

ALTER TABLE Zone
  ADD COLUMN IF NOT EXISTS AreaUnit             text NOT NULL DEFAULT 'SF',
  ADD COLUMN IF NOT EXISTS IsRemainder          boolean NOT NULL DEFAULT false,
  ADD COLUMN IF NOT EXISTS ExcludeFromTotal     boolean NOT NULL DEFAULT false,
  ADD COLUMN IF NOT EXISTS ProjectReservePct    numeric(7,4) NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS DesignReservePct     numeric(7,4) NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS EscalationReservePct numeric(7,4) NOT NULL DEFAULT 0;

-- old free-text unit labels → the fixed list used today
UPDATE Zone SET UnitLabel = CASE
    WHEN UnitLabel IS NULL OR btrim(UnitLabel) = '' THEN 'N/A'
    WHEN lower(UnitLabel) LIKE 'unit%' OR lower(UnitLabel) LIKE 'apart%' OR lower(UnitLabel) LIKE 'room%' THEN 'Unit'
    WHEN lower(UnitLabel) LIKE 'key%'  THEN 'Key'
    WHEN lower(UnitLabel) LIKE 'space%' OR lower(UnitLabel) LIKE 'stall%' OR lower(UnitLabel) LIKE 'park%' THEN 'Space'
    WHEN lower(UnitLabel) = 'mw' THEN 'MW'
    WHEN UnitLabel IN ('N/A', 'Unit', 'MW', 'Key', 'Space') THEN UnitLabel
    ELSE 'N/A' END;
ALTER TABLE Zone ALTER COLUMN UnitLabel SET DEFAULT 'N/A';

DO $$ BEGIN
  ALTER TABLE Zone ADD CONSTRAINT ZoneAreaUnitCheck CHECK (AreaUnit IN ('SF', 'RSF', 'GSF', 'USF'));
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
DO $$ BEGIN
  ALTER TABLE Zone ADD CONSTRAINT ZoneUnitLabelCheck CHECK (UnitLabel IN ('N/A', 'Unit', 'MW', 'Key', 'Space'));
EXCEPTION WHEN duplicate_object THEN NULL; END $$;
CREATE UNIQUE INDEX IF NOT EXISTS UxZoneRemainder ON Zone (VersionId) WHERE IsRemainder;

ALTER TABLE ProjectVersion
  ADD COLUMN IF NOT EXISTS ZonesSetArea     boolean NOT NULL DEFAULT true,   -- "Breakout areas equal total project area"
  ADD COLUMN IF NOT EXISTS PrimaryAreaUnit  text NOT NULL DEFAULT 'SF',
  ADD COLUMN IF NOT EXISTS PrimaryUnitLabel text NOT NULL DEFAULT 'N/A',
  ADD COLUMN IF NOT EXISTS TotalUnits       numeric(12,2) NOT NULL DEFAULT 0;

-- a trade's zone split comes from the picked bid's zone amounts in Scope match when there are any
ALTER TABLE Trade ADD COLUMN IF NOT EXISTS ZonesFromScope boolean NOT NULL DEFAULT false;

INSERT INTO SchemaChange (ChangeNo, ChangeName) VALUES (6, 'Zones like breakout areas: units, remainder, exclude SF, reserve % per zone') ON CONFLICT (ChangeNo) DO NOTHING;
