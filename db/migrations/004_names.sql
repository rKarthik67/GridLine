-- Gridline v2 · migration 004 · new vocabulary for the names users see (old names in comments)
SET search_path = gridline, public;
UPDATE MarkupType SET MarkupName = 'Project overheads' WHERE MarkupCode = 'genreq';    -- was General requirements
UPDATE MarkupType SET MarkupName = 'Precon services'   WHERE MarkupCode = 'precon';    -- was Preconstruction
UPDATE MarkupType SET MarkupName = 'Works cover'       WHERE MarkupCode = 'cover';     -- was Builders risk
UPDATE MarkupType SET MarkupName = 'Project reserve'   WHERE MarkupCode = 'reserveproject';
UPDATE MarkupType SET MarkupName = 'Design reserve'    WHERE MarkupCode = 'reservedesign';
UPDATE MarkupType SET MarkupName = 'Escalation reserve' WHERE MarkupCode = 'reserveescalation';
-- roll-up line names changed: drop the stored roll-up; the API rebuilds it at startup
DELETE FROM RollupLine;
CREATE OR REPLACE VIEW ProjectSummary AS
SELECT p.ProjectId, p.ProjectNo, p.ProjectName, p.Stage, p.ClientName, p.ProjectType, p.City, p.StateCode, p.BidDueOn,
       v.VersionId AS PrimaryVersionId, v.VersionNo AS PrimaryVersionNo, v.VersionLabel AS PrimaryVersionLabel, v.GrossArea,
       (SELECT count(*) FROM ProjectVersion x WHERE x.ProjectId = p.ProjectId) AS Versions,
       (SELECT count(*) FROM Trade t WHERE t.VersionId = v.VersionId AND t.ArchivedAt IS NULL) AS Trades,
       (SELECT count(*) FROM Trade t WHERE t.VersionId = v.VersionId AND t.ArchivedAt IS NULL
          AND EXISTS (SELECT 1 FROM Bid b WHERE b.TradeId = t.TradeId AND b.IsCarried AND b.ArchivedAt IS NULL)) AS TradesCarried,
       (SELECT sum(coalesce(t.CarriedValue, t.LowestValue)) FROM Trade t WHERE t.VersionId = v.VersionId AND t.ArchivedAt IS NULL) AS TradesTotal,
       (SELECT r.Amount FROM RollupLine r WHERE r.VersionId = v.VersionId AND r.LineName = 'Contract total') AS ContractTotal
FROM Project p LEFT JOIN ProjectVersion v ON v.ProjectId = p.ProjectId AND v.IsPrimary
WHERE p.ArchivedAt IS NULL;
INSERT INTO SchemaChange (ChangeNo, ChangeName) VALUES (4, 'New vocabulary: Project overheads, Precon services, Works cover, reserves') ON CONFLICT (ChangeNo) DO NOTHING;

-- seed trades that have no base requirement / zone lines yet get them (what a new trade gets)
INSERT INTO ScopeLine (TradeId, Section, RequirementLineId, SortKey, IsHeading, LineText, Desired)
SELECT t.TradeId, 'baserequirement', r.RequirementLineId, r.SortKey, r.IsHeading, r.LineText, r.Desired
FROM Trade t JOIN ProjectVersion v ON v.VersionId = t.VersionId JOIN RequirementLine r ON r.RequirementSetId = v.RequirementSetId
WHERE t.ArchivedAt IS NULL AND NOT EXISTS (SELECT 1 FROM ScopeLine x WHERE x.TradeId = t.TradeId AND x.Section = 'baserequirement');
INSERT INTO ScopeLine (TradeId, Section, ZoneId, SortKey, LineText)
SELECT t.TradeId, 'zone', z.ZoneId, z.SortOrder, z.ZoneName FROM Trade t JOIN Zone z ON z.VersionId = t.VersionId
WHERE t.ArchivedAt IS NULL AND NOT EXISTS (SELECT 1 FROM ScopeLine x WHERE x.TradeId = t.TradeId AND x.ZoneId = z.ZoneId);
