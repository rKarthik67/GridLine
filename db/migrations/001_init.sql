-- =====================================================================
-- Gridline · database v2 · migration 001
-- One schema (gridline). No underscores in names; unquoted, so any letter case works in queries.
-- First release: projects & versions, trade summary (bid board), scope match, executive summary,
-- project team, general requirements, preconstruction, Excel import, presence, bid portal,
-- AI proposal reader, benchmarks, exports.
-- Later (not created yet): alternates, value engineering, unit prices, allowances, field office.
-- =====================================================================
SET client_min_messages = warning;
CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE EXTENSION IF NOT EXISTS citext;
DROP SCHEMA IF EXISTS gridline CASCADE;
CREATE SCHEMA gridline;
SET search_path = gridline, public;

-- ---------------------------------------------------------------- types
CREATE TYPE ProjectStage   AS ENUM ('lead','pursuit','bidding','submitted','awarded','lost','archived');
CREATE TYPE AccessLevel    AS ENUM ('viewer','editor','owner');
CREATE TYPE BidState       AS ENUM ('invited','bidding','posted','late','incomplete','notplansspecs','undecided','disqualified','declined');
CREATE TYPE LineSection    AS ENUM ('baserequirement','tradeitem','zone');
CREATE TYPE DesiredKind    AS ENUM ('included','excluded','price','text');
CREATE TYPE AnswerKind     AS ENUM ('included','excluded','amount','price','text','blank');
CREATE TYPE RollupKind     AS ENUM ('trades','cost','markup','custom','calculated','tax');
CREATE TYPE CostArea       AS ENUM ('precon','genreq');
CREATE TYPE TeamPhase      AS ENUM ('precon','build','closeout');
CREATE TYPE InviteStatus   AS ENUM ('draft','sent','opened','submitted','declined','expired','withdrawn');
CREATE TYPE ReviewDecision AS ENUM ('pending','accepted','rejected');
CREATE TYPE JobStatus      AS ENUM ('queued','running','done','failed');

CREATE OR REPLACE FUNCTION TouchRow() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN NEW.ChangedAt := now(); NEW.RowRev := OLD.RowRev + 1; RETURN NEW; END $$;

-- =====================================================================
-- People, access, company
-- =====================================================================
CREATE TABLE AppUser (
  UserId        bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  PublicKey     uuid NOT NULL DEFAULT gen_random_uuid() UNIQUE,
  GivenName     text NOT NULL,
  FamilyName    text NOT NULL,
  LoginEmail    citext NOT NULL UNIQUE,
  SsoSubject    text UNIQUE,
  PasswordHash  text,                                 -- local logins only (dev/test); SSO users leave it empty
  Initials      text GENERATED ALWAYS AS (upper(left(GivenName,1) || left(FamilyName,1))) STORED,
  Colour        char(7) NOT NULL DEFAULT '#6A32D4',   -- presence colour
  HomeDivisionId int,
  IsActive      boolean NOT NULL DEFAULT true,
  LastSeenAt    timestamptz,
  CreatedAt     timestamptz NOT NULL DEFAULT now(),
  ChangedAt     timestamptz NOT NULL DEFAULT now(),
  RowRev        int NOT NULL DEFAULT 1
);
CREATE TABLE AccessRole (
  RoleId        smallint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  RoleCode      text NOT NULL UNIQUE,
  RoleName      text NOT NULL,
  Summary       text
);
CREATE TABLE RoleGrant (
  RoleId        smallint NOT NULL REFERENCES AccessRole ON DELETE CASCADE,
  Capability    text NOT NULL,                         -- e.g. 'trade.pick', 'import.run', 'admin.users'
  PRIMARY KEY (RoleId, Capability)
);
CREATE TABLE UserRole (
  UserId        bigint NOT NULL REFERENCES AppUser ON DELETE CASCADE,
  RoleId        smallint NOT NULL REFERENCES AccessRole,
  GrantedAt     timestamptz NOT NULL DEFAULT now(),
  GrantedBy     bigint REFERENCES AppUser,
  PRIMARY KEY (UserId, RoleId)
);
CREATE TABLE LoginToken (                               -- refresh tokens (hashed)
  TokenId       bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  UserId        bigint NOT NULL REFERENCES AppUser ON DELETE CASCADE,
  TokenHash     bytea NOT NULL UNIQUE,
  IssuedAt      timestamptz NOT NULL DEFAULT now(),
  ExpiresAt     timestamptz NOT NULL,
  RevokedAt     timestamptz
);
CREATE TABLE UserPreference (
  UserId        bigint NOT NULL REFERENCES AppUser ON DELETE CASCADE,
  PrefKey       text NOT NULL,                         -- e.g. 'tradeSummary.columns'
  PrefValue     jsonb NOT NULL,
  ChangedAt     timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (UserId, PrefKey)
);
CREATE TABLE CompanyProfile (
  ProfileId     smallint PRIMARY KEY DEFAULT 1 CHECK (ProfileId = 1),
  DisplayName   text NOT NULL,
  LegalName     text,
  TimeZone      text NOT NULL DEFAULT 'America/New_York',
  Currency      char(3) NOT NULL DEFAULT 'USD',
  BrandPrimary  char(7) NOT NULL DEFAULT '#6A32D4',
  BrandAccent   char(7) NOT NULL DEFAULT '#FF7A2F',
  HelpEmail     citext,
  ProjectNoPrefix text NOT NULL DEFAULT 'EST-',
  NextProjectNo int NOT NULL DEFAULT 30001,
  ChangedAt     timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE Division (                                 -- business units
  DivisionId    int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  ShortCode     text NOT NULL UNIQUE,
  DivisionName  text NOT NULL,
  CountsInStaffing boolean NOT NULL DEFAULT true,
  CrmAlias      text
);
ALTER TABLE AppUser ADD CONSTRAINT FkUserDivision FOREIGN KEY (HomeDivisionId) REFERENCES Division;
CREATE TABLE Branch (                                   -- company offices
  BranchId      int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  BranchName    text NOT NULL UNIQUE,
  RegionName    text,
  Street        text, City text, StateCode char(2), PostalCode text
);
CREATE TABLE StaffRole (                                -- employee roles
  StaffRoleId   int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  RoleName      text NOT NULL UNIQUE,
  ShortName     text,
  Seniority     smallint,
  DefaultRate   numeric(10,2),                         -- hourly bill rate
  OpenToAllProjects boolean NOT NULL DEFAULT false
);
CREATE TABLE Staff (                                    -- employees
  StaffId       bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  StaffRoleId   int REFERENCES StaffRole,
  DivisionId    int REFERENCES Division,
  BranchId      int REFERENCES Branch,
  StaffNo       text UNIQUE,
  FullName      text NOT NULL,
  WorkEmail     citext UNIQUE,
  HourlyCost    numeric(10,2),
  BillRate      numeric(10,2),
  IsActive      boolean NOT NULL DEFAULT true
);
CREATE TABLE Setting (
  SettingKey    text PRIMARY KEY,
  SettingName   text NOT NULL,
  Note          text,
  SettingValue  jsonb NOT NULL,
  ChangedAt     timestamptz NOT NULL DEFAULT now(),
  ChangedBy     bigint REFERENCES AppUser
);

-- =====================================================================
-- Reference lists
-- =====================================================================
CREATE TABLE UsState (
  StateCode     char(2) PRIMARY KEY,
  StateName     text NOT NULL,
  SalesTaxRate  numeric(8,5) NOT NULL DEFAULT 0,
  CoverRate     numeric(8,5) NOT NULL DEFAULT 0          -- builders risk style rate per $100
);
CREATE TABLE County (
  CountyId      int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  StateCode     char(2) NOT NULL REFERENCES UsState,
  CountyName    text NOT NULL,
  UNIQUE (StateCode, CountyName)
);
CREATE TABLE CodeEdition (                              -- CSI editions
  EditionId     smallint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  EditionName   text NOT NULL,
  EditionYear   int NOT NULL,
  IsCurrent     boolean NOT NULL DEFAULT false
);
CREATE TABLE CodeGroup (                                -- CSI divisions
  GroupId       int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  EditionId     smallint NOT NULL REFERENCES CodeEdition,
  GroupNo       char(2) NOT NULL,
  GroupTitle    text NOT NULL,
  UNIQUE (EditionId, GroupNo)
);
CREATE TABLE WorkCode (                                 -- CSI codes
  CodeId        int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  GroupId       int NOT NULL REFERENCES CodeGroup,
  CsiCode       text NOT NULL,                          -- '08 44 00'
  CodeTitle     text NOT NULL,
  UNIQUE (GroupId, CsiCode)
);
CREATE TABLE UnitOfMeasure (
  UomCode       text PRIMARY KEY,
  UomName       text NOT NULL
);
CREATE TABLE MarkupType (                               -- indirect cost kinds on the executive summary
  MarkupCode    text PRIMARY KEY,                       -- 'reserveproject', 'fee' ...
  MarkupName    text NOT NULL,
  SortOrder     smallint NOT NULL,
  AppliesTo     text NOT NULL CHECK (AppliesTo IN ('direct','subtotal','contract','tax')),
  DefaultRatePct numeric(8,5) NOT NULL DEFAULT 0
);
CREATE TABLE BondBand (                                 -- performance & payment bond rates
  BandId        int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  FromAmount    bigint NOT NULL,
  ToAmount      bigint,
  RatePerThousand numeric(9,4) NOT NULL
);
CREATE TABLE TravelRate (                               -- per diem by city
  TravelRateId  bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  City          text NOT NULL,
  StateCode     char(2) REFERENCES UsState,
  FiscalYear    smallint NOT NULL,
  LodgingDaily  int NOT NULL,
  MealsDaily    int NOT NULL,
  UNIQUE (City, StateCode, FiscalYear)
);
CREATE TABLE Activity (                                 -- project team tasks
  ActivityId    int GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  Phase         TeamPhase NOT NULL,
  ActivityName  text NOT NULL,
  SortOrder     smallint
);

-- =====================================================================
-- Trade partners (subs)
-- =====================================================================
CREATE TABLE TradePartner (
  PartnerId     bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  PublicKey     uuid NOT NULL DEFAULT gen_random_uuid() UNIQUE,
  CompanyName   text NOT NULL,
  TaxRef        text,
  Website       text,
  IsPrequalified boolean NOT NULL DEFAULT false,
  PrequalExpires date,
  SingleLimit   numeric(16,2),
  AggregateLimit numeric(16,2),
  DiverseTags   text[] NOT NULL DEFAULT '{}',
  AddedByUser   boolean NOT NULL DEFAULT false,          -- shows the * marker
  SearchTerms   text,
  CreatedAt     timestamptz NOT NULL DEFAULT now(),
  ChangedAt     timestamptz NOT NULL DEFAULT now(),
  RowRev        int NOT NULL DEFAULT 1
);
CREATE UNIQUE INDEX UxPartnerName ON TradePartner (lower(CompanyName));
CREATE TABLE PartnerBranch (
  PartnerBranchId bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  PartnerId     bigint NOT NULL REFERENCES TradePartner ON DELETE CASCADE,
  BranchName    text,
  Street        text, City text, StateCode char(2), PostalCode text, Phone text,
  IsHeadOffice  boolean NOT NULL DEFAULT false
);
CREATE TABLE PartnerPerson (                            -- sub contacts
  PersonId      bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  PartnerId     bigint NOT NULL REFERENCES TradePartner ON DELETE CASCADE,
  FullName      text NOT NULL,
  JobTitle      text,
  Email         citext,
  Phone         text,
  IsPrimary     boolean NOT NULL DEFAULT false
);
CREATE TABLE PartnerCode (                              -- trades a partner bids
  PartnerId     bigint NOT NULL REFERENCES TradePartner ON DELETE CASCADE,
  CodeId        int NOT NULL REFERENCES WorkCode,
  PRIMARY KEY (PartnerId, CodeId)
);

-- =====================================================================
-- Projects and versions
-- =====================================================================
CREATE TABLE Project (                                  -- one row per project: what every version shares
  ProjectId     bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  PublicKey     uuid NOT NULL DEFAULT gen_random_uuid() UNIQUE,
  ProjectNo     text NOT NULL UNIQUE,
  ProjectName   text NOT NULL,
  Stage         ProjectStage NOT NULL DEFAULT 'lead',
  ClientName    text,
  ProjectType   text,                                   -- Hospital, Warehouse ... (used for benchmarks)
  ContractType  text,                                   -- GMP, Lump sum ...
  DivisionId    int REFERENCES Division,
  BranchId      int REFERENCES Branch,
  Street        text, City text, StateCode char(2) REFERENCES UsState, CountyName text, PostalCode text,
  BidDueOn      date,
  CrmRef        text,
  CrmValue      numeric(16,2),
  EstimatorId   bigint REFERENCES AppUser,
  ManagerId     bigint REFERENCES AppUser,
  CreatedAt     timestamptz NOT NULL DEFAULT now(),
  CreatedBy     bigint REFERENCES AppUser,
  ChangedAt     timestamptz NOT NULL DEFAULT now(),
  ChangedBy     bigint REFERENCES AppUser,
  ArchivedAt    timestamptz,
  RowRev        int NOT NULL DEFAULT 1
);
CREATE TABLE ProjectVersion (                           -- one row per version (v1 = the original); replaces BidQuote
  VersionId     bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  PublicKey     uuid NOT NULL DEFAULT gen_random_uuid() UNIQUE,
  ProjectId     bigint NOT NULL REFERENCES Project ON DELETE CASCADE,
  VersionNo     int NOT NULL,
  VersionLabel  text,
  IsPrimary     boolean NOT NULL DEFAULT false,
  Status        text NOT NULL DEFAULT 'draft' CHECK (Status IN ('draft','active','submitted','locked')),
  CopiedFromVersionId bigint REFERENCES ProjectVersion,
  EditionId     smallint REFERENCES CodeEdition,
  RequirementSetId bigint,                               -- base requirements checklist (FK below)
  GrossArea     int NOT NULL DEFAULT 0,                  -- SF, sum of zones unless overridden
  BuildMonths   numeric(5,1),
  FeePct        numeric(6,3) NOT NULL DEFAULT 0,
  BaselineName  text,                                    -- which budget the trade summary compares to
  ReadinessPct  numeric(5,2) NOT NULL DEFAULT 0,
  CalcVersion   int NOT NULL DEFAULT 1,
  TotalsHash    text,
  CreatedAt     timestamptz NOT NULL DEFAULT now(),
  CreatedBy     bigint REFERENCES AppUser,
  ChangedAt     timestamptz NOT NULL DEFAULT now(),
  ChangedBy     bigint REFERENCES AppUser,
  RowRev        int NOT NULL DEFAULT 1,
  UNIQUE (ProjectId, VersionNo)
);
CREATE UNIQUE INDEX UxOnePrimaryVersion ON ProjectVersion (ProjectId) WHERE IsPrimary;
CREATE TABLE ProjectMember (                            -- who can open a project
  ProjectId     bigint NOT NULL REFERENCES Project ON DELETE CASCADE,
  UserId        bigint NOT NULL REFERENCES AppUser ON DELETE CASCADE,
  Access        AccessLevel NOT NULL DEFAULT 'editor',
  AddedAt       timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (ProjectId, UserId)
);
CREATE TABLE RecentProject (
  UserId        bigint NOT NULL REFERENCES AppUser ON DELETE CASCADE,
  ProjectId     bigint NOT NULL REFERENCES Project ON DELETE CASCADE,
  OpenedAt      timestamptz NOT NULL DEFAULT now(),
  IsFavourite   boolean NOT NULL DEFAULT false,
  PRIMARY KEY (UserId, ProjectId)
);
CREATE TABLE Zone (                                     -- breakout areas of a version
  ZoneId        bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  VersionId     bigint NOT NULL REFERENCES ProjectVersion ON DELETE CASCADE,
  ZoneCode      text NOT NULL,
  ZoneName      text NOT NULL,
  SortOrder     smallint NOT NULL,
  FloorArea     int NOT NULL DEFAULT 0,
  UnitCount     numeric(12,2),
  UnitLabel     text,
  DefaultShare  numeric(7,4),
  Colour        char(7),
  UNIQUE (VersionId, ZoneCode)
);
CREATE TABLE VersionMarkup (                            -- rates used by the executive summary
  VersionId     bigint NOT NULL REFERENCES ProjectVersion ON DELETE CASCADE,
  MarkupCode    text NOT NULL REFERENCES MarkupType,
  RatePct       numeric(8,5) NOT NULL DEFAULT 0,
  FixedAmount   numeric(16,2),                          -- overrides the rate when set
  BudgetAmount  numeric(16,2),
  PRIMARY KEY (VersionId, MarkupCode)
);
CREATE TABLE Document (                                 -- files in storage (proposals, workbooks, exports)
  DocumentId    bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  PublicKey     uuid NOT NULL DEFAULT gen_random_uuid() UNIQUE,
  ProjectId     bigint REFERENCES Project ON DELETE CASCADE,
  TradeId       bigint,                                  -- FK below
  BidId         bigint,                                  -- FK below
  DocKind       text NOT NULL CHECK (DocKind IN ('proposal','import','export','drawing','other')),
  FileName      text NOT NULL,
  ContentType   text,
  ByteSize      bigint,
  StoragePath   text NOT NULL,
  UploadedBy    bigint REFERENCES AppUser,
  UploadedAt    timestamptz NOT NULL DEFAULT now()
);

-- =====================================================================
-- Templates
-- =====================================================================
CREATE TABLE ScopeKit (                                 -- trade scope templates
  KitId         bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  CodeId        int REFERENCES WorkCode,
  KitName       text NOT NULL,
  IsActive      boolean NOT NULL DEFAULT true,
  ChangedAt     timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE ScopeKitLine (
  KitLineId     bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  KitId         bigint NOT NULL REFERENCES ScopeKit ON DELETE CASCADE,
  SortKey       numeric(12,4) NOT NULL,
  IsHeading     boolean NOT NULL DEFAULT false,
  LineText      text NOT NULL,
  Desired       DesiredKind
);
CREATE TABLE RequirementSet (                           -- base requirements (general scope) checklist
  RequirementSetId bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  SetName       text NOT NULL,
  IsTemplate    boolean NOT NULL DEFAULT true,
  DivisionId    int REFERENCES Division,
  ChangedAt     timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE RequirementLine (
  RequirementLineId bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  RequirementSetId bigint NOT NULL REFERENCES RequirementSet ON DELETE CASCADE,
  SortKey       numeric(12,4) NOT NULL,
  SectionName   text,
  IsHeading     boolean NOT NULL DEFAULT false,
  LineText      text NOT NULL,
  Desired       DesiredKind
);
ALTER TABLE ProjectVersion ADD CONSTRAINT FkVersionRequirementSet FOREIGN KEY (RequirementSetId) REFERENCES RequirementSet;
CREATE TABLE CostKit (                                  -- templates for preconstruction / general requirements
  CostKitId     bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  KitName       text NOT NULL,
  Area          CostArea NOT NULL,
  IsDefault     boolean NOT NULL DEFAULT false
);
CREATE TABLE CostKitItem (
  CostKitItemId bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  CostKitId     bigint NOT NULL REFERENCES CostKit ON DELETE CASCADE,
  CategoryName  text NOT NULL,
  ItemName      text NOT NULL,
  DefaultQty    numeric(12,2),
  UomCode       text REFERENCES UnitOfMeasure,
  DefaultRate   numeric(12,2),
  SortOrder     smallint
);

-- =====================================================================
-- Trade summary (bid board)
-- =====================================================================
CREATE TABLE Trade (
  TradeId       bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  PublicKey     uuid NOT NULL DEFAULT gen_random_uuid() UNIQUE,
  VersionId     bigint NOT NULL REFERENCES ProjectVersion ON DELETE CASCADE,
  ProjectId     bigint NOT NULL REFERENCES Project ON DELETE CASCADE,   -- filled automatically
  CodeId        int REFERENCES WorkCode,
  CsiCode       text NOT NULL,
  TradeName     text NOT NULL,
  ScopeNo       smallint NOT NULL DEFAULT 1,
  KitId         bigint REFERENCES ScopeKit,
  EstimatorId   bigint REFERENCES AppUser,
  BidDueOn      date,
  -- stored results (browser calculates, server confirms)
  LowestValue   numeric(16,2),
  CarriedValue  numeric(16,2),                          -- carried bid total + adjustment, or the plug
  Adjustment    numeric(16,2) NOT NULL DEFAULT 0,
  BudgetValue   numeric(16,2),
  BudgetDelta   numeric(16,2),
  BuyoutPct     numeric(6,3),
  PlugAmount    numeric(16,2),
  ClientNote    text,
  TeamNote      text,
  NeedsRecalc   boolean NOT NULL DEFAULT false,
  SortOrder     int,
  ArchivedAt    timestamptz,
  ChangedAt     timestamptz NOT NULL DEFAULT now(),
  ChangedBy     bigint REFERENCES AppUser,
  RowRev        int NOT NULL DEFAULT 1
);
CREATE UNIQUE INDEX UxTradeCode ON Trade (VersionId, CsiCode, ScopeNo) WHERE ArchivedAt IS NULL;
CREATE INDEX IxTradeProject ON Trade (ProjectId);
CREATE TABLE TradeZone (                                -- split of a trade across zones
  TradeId       bigint NOT NULL REFERENCES Trade ON DELETE CASCADE,
  ZoneId        bigint NOT NULL REFERENCES Zone ON DELETE CASCADE,
  Share         numeric(7,4) NOT NULL,
  PRIMARY KEY (TradeId, ZoneId)
);
CREATE TABLE Bid (                                      -- one sub's bid on a trade (a lane: A, B, C ...)
  BidId         bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  PublicKey     uuid NOT NULL DEFAULT gen_random_uuid() UNIQUE,
  TradeId       bigint NOT NULL REFERENCES Trade ON DELETE CASCADE,
  ProjectId     bigint NOT NULL REFERENCES Project ON DELETE CASCADE,   -- filled automatically
  PartnerId     bigint REFERENCES TradePartner,
  PersonId      bigint REFERENCES PartnerPerson,
  BidderName    text NOT NULL,
  LaneNo        smallint NOT NULL,
  State         BidState NOT NULL DEFAULT 'invited',
  IsPosted      boolean NOT NULL DEFAULT false,
  IsCarried     boolean NOT NULL DEFAULT false,
  BaseAmount    numeric(16,2),
  BaseSource    text,                                   -- manual, import, portal, proposal
  ScopeAdjustments numeric(16,2) NOT NULL DEFAULT 0,
  TotalAmount   numeric(16,2),
  ArchivedAt    timestamptz,
  ChangedAt     timestamptz NOT NULL DEFAULT now(),
  ChangedBy     bigint REFERENCES AppUser,
  RowRev        int NOT NULL DEFAULT 1,
  UNIQUE (TradeId, LaneNo)
);
CREATE UNIQUE INDEX UxOneCarriedBid ON Bid (TradeId) WHERE IsCarried AND ArchivedAt IS NULL;
CREATE INDEX IxBidProject ON Bid (ProjectId);
ALTER TABLE Document ADD CONSTRAINT FkDocumentTrade FOREIGN KEY (TradeId) REFERENCES Trade ON DELETE CASCADE;
ALTER TABLE Document ADD CONSTRAINT FkDocumentBid FOREIGN KEY (BidId) REFERENCES Bid ON DELETE CASCADE;
CREATE TABLE BudgetColumn (                             -- custom budgets to compare against
  BudgetColumnId bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  ProjectId     bigint NOT NULL REFERENCES Project ON DELETE CASCADE,
  BudgetName    text NOT NULL,
  CreatedAt     timestamptz NOT NULL DEFAULT now(),
  UNIQUE (ProjectId, BudgetName)
);
CREATE TABLE BudgetValue (
  BudgetColumnId bigint NOT NULL REFERENCES BudgetColumn ON DELETE CASCADE,
  CsiCode       text NOT NULL,
  Amount        numeric(16,2) NOT NULL,
  PRIMARY KEY (BudgetColumnId, CsiCode)
);
CREATE TABLE IndirectItem (                             -- custom lines on the executive summary
  IndirectId    bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  VersionId     bigint NOT NULL REFERENCES ProjectVersion ON DELETE CASCADE,
  ItemName      text NOT NULL,
  Amount        numeric(16,2) NOT NULL DEFAULT 0,
  OutsideFee    boolean NOT NULL DEFAULT false,
  SortOrder     smallint NOT NULL DEFAULT 1
);

-- =====================================================================
-- Scope match
-- =====================================================================
CREATE TABLE ScopeLine (
  LineId        bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  TradeId       bigint NOT NULL REFERENCES Trade ON DELETE CASCADE,
  Section       LineSection NOT NULL,
  RequirementLineId bigint REFERENCES RequirementLine,
  ZoneId        bigint REFERENCES Zone ON DELETE CASCADE,
  SortKey       numeric(12,4) NOT NULL,                  -- fractional: inserting never renumbers
  IsHeading     boolean NOT NULL DEFAULT false,
  LineText      text NOT NULL,
  Desired       DesiredKind,
  SkipInAdjustments boolean NOT NULL DEFAULT false,
  Indent        smallint NOT NULL DEFAULT 0 CHECK (Indent BETWEEN 0 AND 6),
  LineNote      text,
  ArchivedAt    timestamptz,
  ChangedAt     timestamptz NOT NULL DEFAULT now(),
  ChangedBy     bigint REFERENCES AppUser,
  RowRev        int NOT NULL DEFAULT 1
);
CREATE INDEX IxScopeLineTrade ON ScopeLine (TradeId, Section, SortKey) WHERE ArchivedAt IS NULL;
CREATE TABLE ScopeAnswer (                              -- one cell: a bid's answer to a scope line
  LineId        bigint NOT NULL REFERENCES ScopeLine ON DELETE CASCADE,
  BidId         bigint NOT NULL REFERENCES Bid ON DELETE CASCADE,
  Kind          AnswerKind NOT NULL DEFAULT 'blank',
  Amount        numeric(16,2),
  AnswerText    text,
  Formula       text,
  CellColour    text CHECK (CellColour IN ('red','yellow','green')),
  Note          text,
  Source        text NOT NULL DEFAULT 'manual' CHECK (Source IN ('manual','import','portal','proposal')),
  ChangedAt     timestamptz NOT NULL DEFAULT now(),
  ChangedBy     bigint REFERENCES AppUser,
  RowRev        int NOT NULL DEFAULT 1,
  PRIMARY KEY (LineId, BidId)
);
CREATE INDEX IxScopeAnswerBid ON ScopeAnswer (BidId);
CREATE TABLE EstimateLine (                             -- internal estimate tab
  EstimateLineId bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  TradeId       bigint NOT NULL REFERENCES Trade ON DELETE CASCADE,
  SortKey       numeric(12,4) NOT NULL,
  LineText      text NOT NULL,
  Qty           numeric(14,4), UomCode text REFERENCES UnitOfMeasure, UnitCost numeric(14,4),
  LineTotal     numeric(16,2) GENERATED ALWAYS AS (round(coalesce(Qty,0) * coalesce(UnitCost,0), 2)) STORED
);
CREATE TABLE ScratchSheet (
  TradeId       bigint PRIMARY KEY REFERENCES Trade ON DELETE CASCADE,
  Sheet         jsonb NOT NULL DEFAULT '{}',
  ChangedAt     timestamptz NOT NULL DEFAULT now()
);

-- =====================================================================
-- Executive summary (stored results of the server calculation)
-- =====================================================================
CREATE TABLE RollupLine (
  RollupLineId  bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  VersionId     bigint NOT NULL REFERENCES ProjectVersion ON DELETE CASCADE,
  SortOrder     smallint NOT NULL,
  LineName      text NOT NULL,
  Kind          RollupKind NOT NULL,
  MarkupCode    text REFERENCES MarkupType,
  RatePct       numeric(8,5),
  Amount        numeric(16,2) NOT NULL DEFAULT 0,
  BudgetAmount  numeric(16,2),
  PerArea       numeric(12,4),
  UNIQUE (VersionId, SortOrder)
);
CREATE TABLE RollupZone (
  RollupLineId  bigint NOT NULL REFERENCES RollupLine ON DELETE CASCADE,
  ZoneId        bigint NOT NULL REFERENCES Zone ON DELETE CASCADE,
  Amount        numeric(16,2) NOT NULL,
  PerArea       numeric(12,4),
  PRIMARY KEY (RollupLineId, ZoneId)
);
CREATE TABLE CalcRun (                                  -- browser result checked by the server
  CalcRunId     bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  VersionId     bigint NOT NULL REFERENCES ProjectVersion ON DELETE CASCADE,
  TradeId       bigint,
  Trigger       text NOT NULL,
  ClientHash    text,
  ServerHash    text,
  Matched       boolean,
  RanAt         timestamptz NOT NULL DEFAULT now(),
  DurationMs    int
);

-- =====================================================================
-- Project team, preconstruction, general requirements
-- =====================================================================
CREATE TABLE ProjectWeek (
  WeekId        bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  ProjectId     bigint NOT NULL REFERENCES Project ON DELETE CASCADE,
  WeekNo        smallint NOT NULL,
  StartsOn      date NOT NULL,
  Phase         TeamPhase NOT NULL,
  MonthNo       smallint NOT NULL,
  UNIQUE (ProjectId, WeekNo)
);
CREATE TABLE TeamMember (                               -- a person (or TBA) on the project team
  TeamMemberId  bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  ProjectId     bigint NOT NULL REFERENCES Project ON DELETE CASCADE,
  StaffId       bigint REFERENCES Staff,
  StaffRoleId   int REFERENCES StaffRole,
  RoleLabel     text NOT NULL,
  SortKey       numeric(12,4) NOT NULL,
  HourlyCost    numeric(10,2),
  BillRate      numeric(10,2),
  TravelRateId  bigint REFERENCES TravelRate,
  TravelDays    int NOT NULL DEFAULT 0,
  RelocationCost numeric(14,2) NOT NULL DEFAULT 0,
  Note          text,
  CreatedAt     timestamptz NOT NULL DEFAULT now(),
  ChangedAt     timestamptz NOT NULL DEFAULT now(),
  RowRev        int NOT NULL DEFAULT 1
);
CREATE TABLE TeamMemberWeek (                           -- hours per week (the resource loader)
  TeamMemberId  bigint NOT NULL REFERENCES TeamMember ON DELETE CASCADE,
  WeekId        bigint NOT NULL REFERENCES ProjectWeek ON DELETE CASCADE,
  Hours         numeric(6,2) NOT NULL DEFAULT 0 CHECK (Hours BETWEEN 0 AND 80),
  PRIMARY KEY (TeamMemberId, WeekId)
);
CREATE TABLE TeamMemberActivity (
  TeamMemberId  bigint NOT NULL REFERENCES TeamMember ON DELETE CASCADE,
  ActivityId    int NOT NULL REFERENCES Activity,
  PRIMARY KEY (TeamMemberId, ActivityId)
);
CREATE TABLE CostLine (                                 -- preconstruction and general requirements items
  CostLineId    bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  VersionId     bigint NOT NULL REFERENCES ProjectVersion ON DELETE CASCADE,
  Area          CostArea NOT NULL,
  CategoryName  text NOT NULL,
  ItemName      text NOT NULL,
  SortKey       numeric(12,4) NOT NULL,
  Qty           numeric(12,2) NOT NULL DEFAULT 0,
  UomCode       text REFERENCES UnitOfMeasure,
  Rate          numeric(12,2) NOT NULL DEFAULT 0,       -- actual cost rate
  QuotedRate    numeric(12,2),                          -- what the client sees (null = same as rate)
  IsBillable    boolean NOT NULL DEFAULT true,
  ActualTotal   numeric(16,2) GENERATED ALWAYS AS (round(Qty * Rate, 2)) STORED,
  QuotedTotal   numeric(16,2) GENERATED ALWAYS AS (round(Qty * coalesce(QuotedRate, Rate), 2)) STORED,
  Note          text,
  ChangedAt     timestamptz NOT NULL DEFAULT now(),
  ChangedBy     bigint REFERENCES AppUser,
  RowRev        int NOT NULL DEFAULT 1
);
CREATE INDEX IxCostLineVersion ON CostLine (VersionId, Area, SortKey);

-- =====================================================================
-- Bid portal: subs submit bids online
-- =====================================================================
CREATE TABLE BidInvite (
  InviteId      bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  PublicKey     uuid NOT NULL DEFAULT gen_random_uuid() UNIQUE,
  TradeId       bigint NOT NULL REFERENCES Trade ON DELETE CASCADE,
  BidId         bigint REFERENCES Bid ON DELETE SET NULL,
  PartnerId     bigint NOT NULL REFERENCES TradePartner,
  PersonId      bigint REFERENCES PartnerPerson,
  SentToEmail   citext NOT NULL,
  TokenHash     bytea NOT NULL UNIQUE,                   -- the link token is never stored in plain text
  Status        InviteStatus NOT NULL DEFAULT 'draft',
  Message       text,
  SentAt        timestamptz, OpenedAt timestamptz, SubmittedAt timestamptz,
  ExpiresAt     timestamptz NOT NULL,
  SentBy        bigint REFERENCES AppUser
);
CREATE TABLE BidSubmission (                            -- what the sub entered on the portal
  SubmissionId  bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  InviteId      bigint NOT NULL REFERENCES BidInvite ON DELETE CASCADE,
  BaseAmount    numeric(16,2),
  Answers       jsonb NOT NULL DEFAULT '[]',             -- [{lineId, kind, amount, text}]
  Notes         text,
  SubmittedAt   timestamptz NOT NULL DEFAULT now(),
  Decision      ReviewDecision NOT NULL DEFAULT 'pending',
  DecidedBy     bigint REFERENCES AppUser,
  DecidedAt     timestamptz
);

-- =====================================================================
-- AI proposal reader
-- =====================================================================
CREATE TABLE ProposalReading (
  ReadingId     bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  BidId         bigint NOT NULL REFERENCES Bid ON DELETE CASCADE,
  DocumentId    bigint NOT NULL REFERENCES Document ON DELETE CASCADE,
  Status        JobStatus NOT NULL DEFAULT 'queued',
  ModelName     text,
  BaseAmountFound numeric(16,2),
  Summary       text,
  ErrorText     text,
  StartedAt     timestamptz NOT NULL DEFAULT now(),
  FinishedAt    timestamptz,
  StartedBy     bigint REFERENCES AppUser
);
CREATE TABLE ProposalSuggestion (                       -- one suggested answer, waiting for the estimator
  SuggestionId  bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  ReadingId     bigint NOT NULL REFERENCES ProposalReading ON DELETE CASCADE,
  LineId        bigint REFERENCES ScopeLine ON DELETE CASCADE, -- null = an exclusion with no matching line
  Kind          AnswerKind NOT NULL,
  Amount        numeric(16,2),
  AnswerText    text,
  Evidence      text,                                   -- the quoted words from the proposal
  PageNo        int,
  Confidence    numeric(4,3),
  Decision      ReviewDecision NOT NULL DEFAULT 'pending',
  DecidedBy     bigint REFERENCES AppUser,
  DecidedAt     timestamptz
);

-- =====================================================================
-- Working together, imports, exports, audit
-- =====================================================================
CREATE TABLE Presence (                                 -- who is working where (one row per browser tab)
  ConnectionId  text PRIMARY KEY,                       -- SignalR connection = one tab
  UserId        bigint NOT NULL REFERENCES AppUser ON DELETE CASCADE,
  ProjectId     bigint REFERENCES Project ON DELETE CASCADE,
  VersionId     bigint REFERENCES ProjectVersion ON DELETE CASCADE,
  PageKey       text,                                   -- 'trade-summary', 'scope-match' ...
  FocusKind     text,                                   -- 'trade', 'line', 'answer', 'cost', 'team'
  FocusKey      text,
  Activity      text NOT NULL DEFAULT 'viewing' CHECK (Activity IN ('viewing','editing','typing','idle')),
  JoinedAt      timestamptz NOT NULL DEFAULT now(),
  HeartbeatAt   timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IxPresenceProject ON Presence (ProjectId, HeartbeatAt DESC);
CREATE TABLE EditLock (                                 -- soft lock while someone types in a cell
  LockKey       text PRIMARY KEY,
  UserId        bigint NOT NULL REFERENCES AppUser ON DELETE CASCADE,
  ProjectId     bigint NOT NULL REFERENCES Project ON DELETE CASCADE,
  ConnectionId  text,
  TakenAt       timestamptz NOT NULL DEFAULT now(),
  ExpiresAt     timestamptz NOT NULL
);
CREATE TABLE ActivityEvent (                            -- audit trail + the feed other tabs listen to
  EventId       bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  HappenedAt    timestamptz NOT NULL DEFAULT now(),
  UserId        bigint REFERENCES AppUser,
  ProjectId     bigint REFERENCES Project ON DELETE SET NULL,
  VersionId     bigint,
  Verb          text NOT NULL,                          -- 'bid.carry', 'answer.update', 'import.create' ...
  SubjectType   text NOT NULL,
  SubjectKey    text NOT NULL,
  BeforeState   jsonb, AfterState jsonb
);
CREATE INDEX IxActivityProject ON ActivityEvent (ProjectId, EventId DESC);
CREATE TABLE ImportBatch (                              -- Excel template imports
  BatchId       bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  PublicKey     uuid NOT NULL DEFAULT gen_random_uuid() UNIQUE,
  FileName      text NOT NULL,
  Mode          text NOT NULL CHECK (Mode IN ('newproject','tradesintoversion')),
  TargetProjectId bigint REFERENCES Project ON DELETE SET NULL,
  TargetVersionId bigint REFERENCES ProjectVersion ON DELETE SET NULL,
  Status        text NOT NULL DEFAULT 'uploaded' CHECK (Status IN ('uploaded','checked','created','failed','cancelled')),
  RowCounts     jsonb NOT NULL DEFAULT '{}',
  StartedBy     bigint REFERENCES AppUser,
  StartedAt     timestamptz NOT NULL DEFAULT now(),
  FinishedAt    timestamptz
);
CREATE TABLE ImportIssue (
  IssueId       bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  BatchId       bigint NOT NULL REFERENCES ImportBatch ON DELETE CASCADE,
  SheetName     text NOT NULL,
  RowNo         int,
  ColumnName    text,
  Severity      text NOT NULL CHECK (Severity IN ('error','warning')),
  Message       text NOT NULL
);
CREATE TABLE ExportRun (                                -- every export a user downloads
  ExportId      bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  ProjectId     bigint REFERENCES Project ON DELETE SET NULL,
  VersionId     bigint REFERENCES ProjectVersion ON DELETE SET NULL,
  ExportKind    text NOT NULL,                          -- 'trade-summary.xlsx', 'executive-summary.pdf' ...
  FileName      text NOT NULL,
  ByteSize      bigint,
  DurationMs    int,
  RequestedBy   bigint REFERENCES AppUser,
  RequestedAt   timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE SchemaChange (
  ChangeNo      int PRIMARY KEY,
  ChangeName    text NOT NULL,
  AppliedAt     timestamptz NOT NULL DEFAULT now()
);

-- ---------------------------------------------------------------- triggers
CREATE OR REPLACE FUNCTION SetProjectId() RETURNS trigger LANGUAGE plpgsql AS $$
BEGIN
  IF TG_TABLE_NAME = 'trade' THEN
    SELECT v.ProjectId INTO NEW.ProjectId FROM gridline.ProjectVersion v WHERE v.VersionId = NEW.VersionId;
  ELSE
    SELECT t.ProjectId INTO NEW.ProjectId FROM gridline.Trade t WHERE t.TradeId = NEW.TradeId;
  END IF;
  RETURN NEW;
END $$;
CREATE TRIGGER TrgTradeProject BEFORE INSERT OR UPDATE OF VersionId ON Trade FOR EACH ROW EXECUTE FUNCTION SetProjectId();
CREATE TRIGGER TrgBidProject   BEFORE INSERT OR UPDATE OF TradeId ON Bid FOR EACH ROW EXECUTE FUNCTION SetProjectId();
DO $$ DECLARE t text; BEGIN
  FOREACH t IN ARRAY ARRAY['AppUser','Project','ProjectVersion','TradePartner','Trade','Bid','ScopeLine','ScopeAnswer','TeamMember','CostLine'] LOOP
    EXECUTE format('CREATE TRIGGER TrgTouch BEFORE UPDATE ON gridline.%s FOR EACH ROW EXECUTE FUNCTION gridline.TouchRow()', t);
  END LOOP; END $$;

-- ---------------------------------------------------------------- views
CREATE VIEW ProjectSummary AS
SELECT p.ProjectId, p.ProjectNo, p.ProjectName, p.Stage, p.ClientName, p.ProjectType, p.City, p.StateCode, p.BidDueOn,
       v.VersionId AS PrimaryVersionId, v.VersionNo AS PrimaryVersionNo, v.VersionLabel AS PrimaryVersionLabel, v.GrossArea,
       (SELECT count(*) FROM ProjectVersion x WHERE x.ProjectId = p.ProjectId) AS Versions,
       (SELECT count(*) FROM Trade t WHERE t.VersionId = v.VersionId AND t.ArchivedAt IS NULL) AS Trades,
       (SELECT count(*) FROM Trade t WHERE t.VersionId = v.VersionId AND t.ArchivedAt IS NULL
          AND EXISTS (SELECT 1 FROM Bid b WHERE b.TradeId = t.TradeId AND b.IsCarried)) AS TradesCarried,
       (SELECT sum(coalesce(t.CarriedValue, t.LowestValue)) FROM Trade t WHERE t.VersionId = v.VersionId AND t.ArchivedAt IS NULL) AS TradesTotal,
       (SELECT r.Amount FROM RollupLine r WHERE r.VersionId = v.VersionId AND r.LineName = 'Contract Total') AS ContractTotal
FROM Project p LEFT JOIN ProjectVersion v ON v.ProjectId = p.ProjectId AND v.IsPrimary
WHERE p.ArchivedAt IS NULL;

CREATE VIEW ProjectTrades AS
SELECT p.ProjectId, p.ProjectNo, p.ProjectName, v.VersionId, v.VersionNo, v.IsPrimary AS IsPrimaryVersion,
       t.TradeId, t.CsiCode, t.TradeName, t.BudgetValue AS Budget, t.LowestValue AS Lowest, t.CarriedValue,
       cb.BidderName AS CarriedBidder, (SELECT count(*) FROM Bid b WHERE b.TradeId = t.TradeId AND b.ArchivedAt IS NULL) AS Bids,
       t.PlugAmount, t.ClientNote, t.TeamNote
FROM Trade t JOIN ProjectVersion v ON v.VersionId = t.VersionId JOIN Project p ON p.ProjectId = v.ProjectId
LEFT JOIN Bid cb ON cb.TradeId = t.TradeId AND cb.IsCarried AND cb.ArchivedAt IS NULL
WHERE t.ArchivedAt IS NULL;

CREATE VIEW TradeBids AS
SELECT p.ProjectId, p.ProjectNo, p.ProjectName, v.VersionId, v.VersionNo, v.IsPrimary AS IsPrimaryVersion,
       t.TradeId, t.CsiCode, t.TradeName, b.BidId, b.LaneNo, chr(64 + b.LaneNo) AS Lane, b.BidderName, tp.CompanyName,
       b.State, b.IsPosted, b.IsCarried, b.BaseAmount, b.ScopeAdjustments, b.TotalAmount,
       CASE WHEN b.IsPosted THEN rank() OVER (PARTITION BY t.TradeId, b.IsPosted ORDER BY b.TotalAmount) END AS PriceRank
FROM Bid b JOIN Trade t ON t.TradeId = b.TradeId JOIN ProjectVersion v ON v.VersionId = t.VersionId
JOIN Project p ON p.ProjectId = v.ProjectId LEFT JOIN TradePartner tp ON tp.PartnerId = b.PartnerId
WHERE t.ArchivedAt IS NULL AND b.ArchivedAt IS NULL;

CREATE VIEW ScopeAnswers AS
SELECT p.ProjectName, v.VersionNo, t.TradeId, t.CsiCode, t.TradeName, l.LineId, l.Section, l.SortKey, l.LineText, l.Desired,
       b.BidId, chr(64 + b.LaneNo) AS Lane, b.BidderName, coalesce(a.Kind::text,'blank') AS AnswerKind, a.Amount, a.AnswerText, a.CellColour, a.Source
FROM ScopeLine l JOIN Trade t ON t.TradeId = l.TradeId JOIN ProjectVersion v ON v.VersionId = t.VersionId
JOIN Project p ON p.ProjectId = v.ProjectId JOIN Bid b ON b.TradeId = t.TradeId AND b.ArchivedAt IS NULL
LEFT JOIN ScopeAnswer a ON a.LineId = l.LineId AND a.BidId = b.BidId
WHERE l.ArchivedAt IS NULL AND NOT l.IsHeading;

-- Benchmarks from past projects: $/SF per CSI code and project type (primary versions only)
CREATE VIEW TradeBenchmark AS
SELECT t.CsiCode, p.ProjectType, count(*) AS Projects,
       round(min(t.CarriedValue / nullif(v.GrossArea,0)), 2) AS LowPerSf,
       round((percentile_cont(0.5) WITHIN GROUP (ORDER BY t.CarriedValue / nullif(v.GrossArea,0)))::numeric, 2) AS MedianPerSf,
       round(max(t.CarriedValue / nullif(v.GrossArea,0)), 2) AS HighPerSf
FROM Trade t JOIN ProjectVersion v ON v.VersionId = t.VersionId AND v.IsPrimary JOIN Project p ON p.ProjectId = v.ProjectId
WHERE t.ArchivedAt IS NULL AND t.CarriedValue > 0 AND v.GrossArea > 0 AND p.Stage IN ('submitted','awarded','lost','bidding')
GROUP BY t.CsiCode, p.ProjectType;

CREATE VIEW WhoIsHere AS
SELECT p.ConnectionId, p.ProjectId, p.VersionId, p.PageKey, p.FocusKind, p.FocusKey, p.Activity,
       u.UserId, u.GivenName || ' ' || u.FamilyName AS PersonName, u.Initials, u.Colour, p.HeartbeatAt
FROM Presence p JOIN AppUser u ON u.UserId = p.UserId
WHERE p.HeartbeatAt > now() - interval '45 seconds';

INSERT INTO SchemaChange (ChangeNo, ChangeName) VALUES (1, 'Gridline v2: single schema, first-release scope');
