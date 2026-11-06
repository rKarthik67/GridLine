-- Gridline v2 · migration 009 · companies (tenants) for the hosted product
--  * Company: one row per customer. Every user, project, trade partner, employee and template belongs to one company.
--    The existing CompanyProfile row becomes company 1; a "Gridline library" company holds the starter templates
--    (scope kits, base requirement checklists, cost kits, qualification sets, employee roles, precon tasks) that a
--    new company gets a copy of when it signs up (CopyCompanyLibrary).
--  * Names that used to be unique across the database (project numbers, trade partner names, employee roles,
--    divisions, branches, qualification sets) are now unique per company. Login emails stay unique: one email = one person.
--  * Sign-up, invitations and password resets: UserInvite, PasswordReset; failed-login lockout on AppUser.
--  * Reference data that is the same for everyone (US states, CSI codes, units, per diem, markup types) stays shared.
SET search_path = gridline, public;

-- ---------------------------------------------------------------- companies
CREATE TABLE IF NOT EXISTS Company (
  CompanyId     bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  PublicKey     uuid NOT NULL DEFAULT gen_random_uuid() UNIQUE,
  Slug          text NOT NULL UNIQUE,                     -- short handle in links and sign-up: 'northshore'
  DisplayName   text NOT NULL,
  LegalName     text,
  TimeZone      text NOT NULL DEFAULT 'America/New_York',
  Currency      char(3) NOT NULL DEFAULT 'USD',
  BrandPrimary  char(7) NOT NULL DEFAULT '#27624d',
  BrandAccent   char(7) NOT NULL DEFAULT '#d8ef58',
  HelpEmail     citext,
  ProjectNoPrefix text NOT NULL DEFAULT 'EST-',
  NextProjectNo int NOT NULL DEFAULT 1001,
  Plan          text NOT NULL DEFAULT 'trial' CHECK (Plan IN ('trial','team','business','enterprise','internal')),
  Status        text NOT NULL DEFAULT 'active' CHECK (Status IN ('active','suspended','closed')),
  IsLibrary     boolean NOT NULL DEFAULT false,            -- the starter-template company; nobody signs in to it
  TrialEndsOn   date,
  CreatedAt     timestamptz NOT NULL DEFAULT now(),
  ChangedAt     timestamptz NOT NULL DEFAULT now(),
  RowRev        int NOT NULL DEFAULT 1
);
DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_trigger WHERE tgname = 'trgtouch' AND tgrelid = 'gridline.company'::regclass) THEN
    CREATE TRIGGER TrgTouch BEFORE UPDATE ON Company FOR EACH ROW EXECUTE FUNCTION TouchRow();
  END IF;
END $$;

-- company 1 = the company this database was set up for (CompanyProfile), company 2 = the starter library
INSERT INTO Company (Slug, DisplayName, LegalName, TimeZone, Currency, BrandPrimary, BrandAccent, HelpEmail, ProjectNoPrefix, NextProjectNo, Plan)
SELECT 'northshore', DisplayName, LegalName, TimeZone, Currency, '#27624d', '#d8ef58', HelpEmail, ProjectNoPrefix, NextProjectNo, 'internal'
FROM CompanyProfile WHERE NOT EXISTS (SELECT 1 FROM Company);
INSERT INTO Company (Slug, DisplayName, Plan, Status, IsLibrary)
SELECT 'library', 'Gridline starter library', 'internal', 'active', true WHERE NOT EXISTS (SELECT 1 FROM Company WHERE IsLibrary);

-- ---------------------------------------------------------------- CompanyId on everything a company owns
DO $$ DECLARE t text; BEGIN
  FOREACH t IN ARRAY ARRAY['AppUser','Project','TradePartner','Staff','StaffRole','Division','Branch','ScopeKit','RequirementSet','CostKit','QualificationSet','Activity'] LOOP
    -- names are unquoted (lower case) everywhere in this schema, so %s not %I
    EXECUTE format('ALTER TABLE gridline.%s ADD COLUMN IF NOT EXISTS CompanyId bigint', t);
    EXECUTE format('UPDATE gridline.%s SET CompanyId = 1 WHERE CompanyId IS NULL', t);
    EXECUTE format('ALTER TABLE gridline.%s ALTER COLUMN CompanyId SET NOT NULL', t);
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = lower('Fk' || t || 'Company')) THEN
      EXECUTE format('ALTER TABLE gridline.%s ADD CONSTRAINT %s FOREIGN KEY (CompanyId) REFERENCES gridline.Company', t, 'Fk' || t || 'Company');
    END IF;
    EXECUTE format('CREATE INDEX IF NOT EXISTS %s ON gridline.%s (CompanyId)', 'Ix' || t || 'Company', t);
  END LOOP;
END $$;

-- uniqueness is now per company
ALTER TABLE Project DROP CONSTRAINT IF EXISTS project_projectno_key;
CREATE UNIQUE INDEX IF NOT EXISTS UxProjectNo ON Project (CompanyId, ProjectNo);
DROP INDEX IF EXISTS UxPartnerName;
CREATE UNIQUE INDEX IF NOT EXISTS UxPartnerName ON TradePartner (CompanyId, lower(CompanyName));
ALTER TABLE Staff DROP CONSTRAINT IF EXISTS staff_staffno_key;
ALTER TABLE Staff DROP CONSTRAINT IF EXISTS staff_workemail_key;
CREATE UNIQUE INDEX IF NOT EXISTS UxStaffNo ON Staff (CompanyId, StaffNo) WHERE StaffNo IS NOT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS UxStaffEmail ON Staff (CompanyId, WorkEmail) WHERE WorkEmail IS NOT NULL;
ALTER TABLE StaffRole DROP CONSTRAINT IF EXISTS staffrole_rolename_key;
CREATE UNIQUE INDEX IF NOT EXISTS UxStaffRoleName ON StaffRole (CompanyId, RoleName);
ALTER TABLE Division DROP CONSTRAINT IF EXISTS division_shortcode_key;
CREATE UNIQUE INDEX IF NOT EXISTS UxDivisionCode ON Division (CompanyId, ShortCode);
ALTER TABLE Branch DROP CONSTRAINT IF EXISTS branch_branchname_key;
CREATE UNIQUE INDEX IF NOT EXISTS UxBranchName ON Branch (CompanyId, BranchName);
ALTER TABLE QualificationSet DROP CONSTRAINT IF EXISTS qualificationset_setname_key;
CREATE UNIQUE INDEX IF NOT EXISTS UxQualificationSetName ON QualificationSet (CompanyId, SetName);

-- ---------------------------------------------------------------- sign-up, invitations, password resets, lockout
ALTER TABLE AppUser
  ADD COLUMN IF NOT EXISTS FailedLogins   int NOT NULL DEFAULT 0,
  ADD COLUMN IF NOT EXISTS LockedUntil    timestamptz,
  ADD COLUMN IF NOT EXISTS PasswordChangedAt timestamptz,
  ADD COLUMN IF NOT EXISTS EmailVerifiedAt   timestamptz,
  ADD COLUMN IF NOT EXISTS JobTitle       text;
CREATE TABLE IF NOT EXISTS UserInvite (
  InviteId      bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  CompanyId     bigint NOT NULL REFERENCES Company ON DELETE CASCADE,
  Email         citext NOT NULL,
  GivenName     text, FamilyName text,
  RoleCode      text NOT NULL DEFAULT 'estimator',
  TokenHash     bytea NOT NULL UNIQUE,                     -- the link token is never stored in plain text
  InvitedBy     bigint REFERENCES AppUser,
  CreatedAt     timestamptz NOT NULL DEFAULT now(),
  ExpiresAt     timestamptz NOT NULL,
  AcceptedAt    timestamptz,
  UserId        bigint REFERENCES AppUser
);
CREATE INDEX IF NOT EXISTS IxUserInviteCompany ON UserInvite (CompanyId, Email);
CREATE TABLE IF NOT EXISTS PasswordReset (
  ResetId       bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  UserId        bigint NOT NULL REFERENCES AppUser ON DELETE CASCADE,
  TokenHash     bytea NOT NULL UNIQUE,
  CreatedAt     timestamptz NOT NULL DEFAULT now(),
  ExpiresAt     timestamptz NOT NULL,
  UsedAt        timestamptz
);
ALTER TABLE LoginToken ADD COLUMN IF NOT EXISTS LastUsedAt timestamptz, ADD COLUMN IF NOT EXISTS UserAgent text;

-- ---------------------------------------------------------------- views now carry the company
CREATE OR REPLACE VIEW ProjectSummary AS
SELECT p.ProjectId, p.ProjectNo, p.ProjectName, p.Stage, p.ClientName, p.ProjectType, p.City, p.StateCode, p.BidDueOn,
       v.VersionId AS PrimaryVersionId, v.VersionNo AS PrimaryVersionNo, v.VersionLabel AS PrimaryVersionLabel, v.GrossArea,
       (SELECT count(*) FROM ProjectVersion x WHERE x.ProjectId = p.ProjectId) AS Versions,
       (SELECT count(*) FROM Trade t WHERE t.VersionId = v.VersionId AND t.ArchivedAt IS NULL) AS Trades,
       (SELECT count(*) FROM Trade t WHERE t.VersionId = v.VersionId AND t.ArchivedAt IS NULL
          AND EXISTS (SELECT 1 FROM Bid b WHERE b.TradeId = t.TradeId AND b.IsCarried AND b.ArchivedAt IS NULL)) AS TradesCarried,
       (SELECT sum(coalesce(t.CarriedValue, t.LowestValue)) FROM Trade t WHERE t.VersionId = v.VersionId AND t.ArchivedAt IS NULL) AS TradesTotal,
       (SELECT r.Amount FROM RollupLine r WHERE r.VersionId = v.VersionId AND r.LineName = 'Contract total') AS ContractTotal,
       p.CompanyId, p.EstimatorId, p.CreatedAt, p.ChangedAt
FROM Project p LEFT JOIN ProjectVersion v ON v.ProjectId = p.ProjectId AND v.IsPrimary
WHERE p.ArchivedAt IS NULL;

-- benchmarks never cross companies: a company only sees $/SF ranges from its own past projects
CREATE OR REPLACE VIEW TradeBenchmark AS
SELECT t.CsiCode, p.ProjectType, count(*) AS Projects,
       round(min(t.CarriedValue / nullif(v.GrossArea,0)), 2) AS LowPerSf,
       round((percentile_cont(0.5) WITHIN GROUP (ORDER BY t.CarriedValue / nullif(v.GrossArea,0)))::numeric, 2) AS MedianPerSf,
       round(max(t.CarriedValue / nullif(v.GrossArea,0)), 2) AS HighPerSf,
       p.CompanyId
FROM Trade t JOIN ProjectVersion v ON v.VersionId = t.VersionId AND v.IsPrimary JOIN Project p ON p.ProjectId = v.ProjectId
WHERE t.ArchivedAt IS NULL AND t.CarriedValue > 0 AND v.GrossArea > 0 AND p.Stage IN ('submitted','awarded','lost','bidding')
GROUP BY t.CsiCode, p.ProjectType, p.CompanyId;

-- ---------------------------------------------------------------- starter library
CREATE OR REPLACE FUNCTION CopyCompanyLibrary(src bigint, dst bigint) RETURNS void LANGUAGE plpgsql AS $$
BEGIN
  INSERT INTO StaffRole (CompanyId, RoleName, ShortName, Seniority, DefaultRate, OpenToAllProjects)
  SELECT dst, r.RoleName, r.ShortName, r.Seniority, r.DefaultRate, r.OpenToAllProjects FROM StaffRole r
  WHERE r.CompanyId = src AND NOT EXISTS (SELECT 1 FROM StaffRole x WHERE x.CompanyId = dst AND x.RoleName = r.RoleName);

  INSERT INTO ScopeKit (CompanyId, CodeId, KitName, IsActive)
  SELECT dst, k.CodeId, k.KitName, true FROM ScopeKit k
  WHERE k.CompanyId = src AND k.IsActive AND NOT EXISTS (SELECT 1 FROM ScopeKit x WHERE x.CompanyId = dst AND x.KitName = k.KitName);
  INSERT INTO ScopeKitLine (KitId, SortKey, IsHeading, LineText, Desired)
  SELECT n.KitId, l.SortKey, l.IsHeading, l.LineText, l.Desired
  FROM ScopeKit o JOIN ScopeKit n ON n.CompanyId = dst AND n.KitName = o.KitName JOIN ScopeKitLine l ON l.KitId = o.KitId
  WHERE o.CompanyId = src AND NOT EXISTS (SELECT 1 FROM ScopeKitLine x WHERE x.KitId = n.KitId);

  INSERT INTO RequirementSet (CompanyId, SetName, IsTemplate)
  SELECT dst, r.SetName, true FROM RequirementSet r
  WHERE r.CompanyId = src AND r.IsTemplate AND NOT EXISTS (SELECT 1 FROM RequirementSet x WHERE x.CompanyId = dst AND x.IsTemplate AND x.SetName = r.SetName);
  INSERT INTO RequirementLine (RequirementSetId, SortKey, SectionName, IsHeading, LineText, Desired)
  SELECT n.RequirementSetId, l.SortKey, l.SectionName, l.IsHeading, l.LineText, l.Desired
  FROM RequirementSet o JOIN RequirementSet n ON n.CompanyId = dst AND n.IsTemplate AND n.SetName = o.SetName JOIN RequirementLine l ON l.RequirementSetId = o.RequirementSetId
  WHERE o.CompanyId = src AND o.IsTemplate AND NOT EXISTS (SELECT 1 FROM RequirementLine x WHERE x.RequirementSetId = n.RequirementSetId);

  INSERT INTO CostKit (CompanyId, KitName, Area, IsDefault)
  SELECT dst, k.KitName, k.Area, k.IsDefault FROM CostKit k
  WHERE k.CompanyId = src AND NOT EXISTS (SELECT 1 FROM CostKit x WHERE x.CompanyId = dst AND x.KitName = k.KitName AND x.Area = k.Area);
  INSERT INTO CostKitItem (CostKitId, CategoryName, ItemName, DefaultQty, UomCode, DefaultRate, SortOrder)
  SELECT n.CostKitId, i.CategoryName, i.ItemName, i.DefaultQty, i.UomCode, i.DefaultRate, i.SortOrder
  FROM CostKit o JOIN CostKit n ON n.CompanyId = dst AND n.KitName = o.KitName AND n.Area = o.Area JOIN CostKitItem i ON i.CostKitId = o.CostKitId
  WHERE o.CompanyId = src AND NOT EXISTS (SELECT 1 FROM CostKitItem x WHERE x.CostKitId = n.CostKitId);

  INSERT INTO QualificationSet (CompanyId, SetName, CodeId, IsActive)
  SELECT dst, s.SetName, s.CodeId, true FROM QualificationSet s
  WHERE s.CompanyId = src AND s.IsActive AND NOT EXISTS (SELECT 1 FROM QualificationSet x WHERE x.CompanyId = dst AND x.SetName = s.SetName);
  INSERT INTO QualificationSetLine (QualificationSetId, Kind, SortOrder, QualText)
  SELECT n.QualificationSetId, l.Kind, l.SortOrder, l.QualText
  FROM QualificationSet o JOIN QualificationSet n ON n.CompanyId = dst AND n.SetName = o.SetName JOIN QualificationSetLine l ON l.QualificationSetId = o.QualificationSetId
  WHERE o.CompanyId = src AND NOT EXISTS (SELECT 1 FROM QualificationSetLine x WHERE x.QualificationSetId = n.QualificationSetId);

  INSERT INTO Activity (CompanyId, Phase, ActivityName, SortOrder, IsDefault, IsActive)
  SELECT dst, a.Phase, a.ActivityName, a.SortOrder, a.IsDefault, a.IsActive FROM Activity a
  WHERE a.CompanyId = src AND a.ProjectId IS NULL AND NOT EXISTS (SELECT 1 FROM Activity x WHERE x.CompanyId = dst AND x.ProjectId IS NULL AND x.Phase = a.Phase AND x.ActivityName = a.ActivityName);
END $$;

-- the library gets a copy of company 1's templates (first run only)
SELECT CopyCompanyLibrary(1, (SELECT CompanyId FROM Company WHERE IsLibrary))
WHERE NOT EXISTS (SELECT 1 FROM SchemaChange WHERE ChangeNo = 9);

INSERT INTO SchemaChange (ChangeNo, ChangeName) VALUES (9, 'Companies (tenants), starter library, invitations, password resets, lockout') ON CONFLICT (ChangeNo) DO NOTHING;
