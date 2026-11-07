namespace Gridline.Infrastructure;

/// <summary>Version copying, budgets and standard scope lines — shared by several endpoints.</summary>
public static class Versions
{
    /// <summary>
    /// Copies everything priced in a version into another (empty) version: zones, rates, trades with their
    /// zone split, bids, scope lines and answers, estimate tabs, scratch pads, indirect items and cost lines.
    /// New ids are reserved up front so the copy is set-based (fast even for big projects).
    /// </summary>
    public static async Task CopyContent(Work w, long from, long to)
    {
        var p = new { from, to, u = w.User.UserId };
        await w.Exec("""
            DROP TABLE IF EXISTS mz, mt, mb, ml;
            CREATE TEMP TABLE mz ON COMMIT DROP AS SELECT ZoneId AS old, nextval(pg_get_serial_sequence('gridline.zone','zoneid')) AS new FROM Zone WHERE VersionId = @from;
            CREATE TEMP TABLE mt ON COMMIT DROP AS SELECT TradeId AS old, nextval(pg_get_serial_sequence('gridline.trade','tradeid')) AS new FROM Trade WHERE VersionId = @from AND ArchivedAt IS NULL;
            CREATE TEMP TABLE mb ON COMMIT DROP AS SELECT b.BidId AS old, nextval(pg_get_serial_sequence('gridline.bid','bidid')) AS new FROM Bid b JOIN mt ON mt.old = b.TradeId WHERE b.ArchivedAt IS NULL;
            CREATE TEMP TABLE ml ON COMMIT DROP AS SELECT l.LineId AS old, nextval(pg_get_serial_sequence('gridline.scopeline','lineid')) AS new FROM ScopeLine l JOIN mt ON mt.old = l.TradeId WHERE l.ArchivedAt IS NULL;
            """, p);
        await w.Exec("""
            INSERT INTO Zone (ZoneId, VersionId, ZoneCode, ZoneName, SortOrder, FloorArea, UnitCount, UnitLabel, DefaultShare, Colour,
                              AreaUnit, IsRemainder, ExcludeFromTotal, ProjectReservePct, DesignReservePct, EscalationReservePct) OVERRIDING SYSTEM VALUE
            SELECT mz.new, @to, z.ZoneCode, z.ZoneName, z.SortOrder, z.FloorArea, z.UnitCount, z.UnitLabel, z.DefaultShare, z.Colour,
                   z.AreaUnit, z.IsRemainder, z.ExcludeFromTotal, z.ProjectReservePct, z.DesignReservePct, z.EscalationReservePct FROM Zone z JOIN mz ON mz.old = z.ZoneId;

            INSERT INTO VersionMarkup (VersionId, MarkupCode, RatePct, FixedAmount, BudgetAmount, IsOff)
            SELECT @to, MarkupCode, RatePct, FixedAmount, BudgetAmount, IsOff FROM VersionMarkup WHERE VersionId = @from
            ON CONFLICT (VersionId, MarkupCode) DO UPDATE SET RatePct = EXCLUDED.RatePct, FixedAmount = EXCLUDED.FixedAmount, BudgetAmount = EXCLUDED.BudgetAmount;

            INSERT INTO Trade (TradeId, VersionId, CodeId, CsiCode, TradeName, ScopeNo, KitId, EstimatorId, BidDueOn, LowestValue, CarriedValue, Adjustment,
                               BudgetValue, BudgetDelta, BuyoutPct, PlugAmount, ClientNote, TeamNote, SortOrder, ChangedBy) OVERRIDING SYSTEM VALUE
            SELECT mt.new, @to, t.CodeId, t.CsiCode, t.TradeName, t.ScopeNo, t.KitId, t.EstimatorId, t.BidDueOn, t.LowestValue, t.CarriedValue, t.Adjustment,
                   t.BudgetValue, t.BudgetDelta, t.BuyoutPct, t.PlugAmount, t.ClientNote, t.TeamNote, t.SortOrder, @u
            FROM Trade t JOIN mt ON mt.old = t.TradeId;

            INSERT INTO TradeZone (TradeId, ZoneId, Share)
            SELECT mt.new, mz.new, tz.Share FROM TradeZone tz JOIN mt ON mt.old = tz.TradeId JOIN mz ON mz.old = tz.ZoneId;

            INSERT INTO Bid (BidId, TradeId, PartnerId, PersonId, BidderName, LaneNo, State, IsPosted, IsCarried, BaseAmount, BaseSource, ScopeAdjustments, TotalAmount, ChangedBy) OVERRIDING SYSTEM VALUE
            SELECT mb.new, mt.new, b.PartnerId, b.PersonId, b.BidderName, b.LaneNo, b.State, b.IsPosted, b.IsCarried, b.BaseAmount, b.BaseSource, b.ScopeAdjustments, b.TotalAmount, @u
            FROM Bid b JOIN mb ON mb.old = b.BidId JOIN mt ON mt.old = b.TradeId;

            INSERT INTO ScopeLine (LineId, TradeId, Section, RequirementLineId, ZoneId, SortKey, IsHeading, LineText, Desired, SkipInAdjustments, Indent, LineNote, ChangedBy) OVERRIDING SYSTEM VALUE
            SELECT ml.new, mt.new, l.Section, l.RequirementLineId, mz.new, l.SortKey, l.IsHeading, l.LineText, l.Desired, l.SkipInAdjustments, l.Indent, l.LineNote, @u
            FROM ScopeLine l JOIN ml ON ml.old = l.LineId JOIN mt ON mt.old = l.TradeId LEFT JOIN mz ON mz.old = l.ZoneId;

            INSERT INTO ScopeAnswer (LineId, BidId, Kind, Amount, AnswerText, Formula, CellColour, Note, Source, ChangedBy)
            SELECT ml.new, mb.new, a.Kind, a.Amount, a.AnswerText, a.Formula, a.CellColour, a.Note, a.Source, @u
            FROM ScopeAnswer a JOIN ml ON ml.old = a.LineId JOIN mb ON mb.old = a.BidId;

            INSERT INTO EstimateLine (TradeId, SortKey, LineText, Qty, UomCode, UnitCost)
            SELECT mt.new, e.SortKey, e.LineText, e.Qty, e.UomCode, e.UnitCost FROM EstimateLine e JOIN mt ON mt.old = e.TradeId;

            INSERT INTO ScratchSheet (TradeId, Sheet) SELECT mt.new, s.Sheet FROM ScratchSheet s JOIN mt ON mt.old = s.TradeId;

            INSERT INTO IndirectItem (VersionId, ItemName, Amount, OutsideFee, SortOrder, Description, IsOff)
            SELECT @to, ItemName, Amount, OutsideFee, SortOrder, Description, IsOff FROM IndirectItem WHERE VersionId = @from;

            INSERT INTO CostLine (VersionId, Area, CategoryName, ItemName, SortKey, Qty, UomCode, Rate, QuotedRate, IsBillable, Note, CsiCode, ChangedBy)
            SELECT @to, Area, CategoryName, ItemName, SortKey, Qty, UomCode, Rate, QuotedRate, IsBillable, Note, CsiCode, @u FROM CostLine WHERE VersionId = @from;

            INSERT INTO VersionTask (VersionId, ActivityId, SortOrder) SELECT @to, ActivityId, SortOrder FROM VersionTask WHERE VersionId = @from;
            CREATE TEMP TABLE mpm ON COMMIT DROP AS SELECT PreconMemberId AS old, nextval(pg_get_serial_sequence('gridline.preconmember','preconmemberid')) AS new FROM PreconMember WHERE VersionId = @from;
            INSERT INTO PreconMember (PreconMemberId, VersionId, StaffId, StaffRoleId, RoleLabel, TeamGroup, SortKey, PostingRate, QuotedRate) OVERRIDING SYSTEM VALUE
            SELECT mpm.new, @to, p.StaffId, p.StaffRoleId, p.RoleLabel, p.TeamGroup, p.SortKey, p.PostingRate, p.QuotedRate FROM PreconMember p JOIN mpm ON mpm.old = p.PreconMemberId;
            INSERT INTO PreconMemberTask (PreconMemberId, ActivityId, Hours) SELECT mpm.new, t.ActivityId, t.Hours FROM PreconMemberTask t JOIN mpm ON mpm.old = t.PreconMemberId;

            -- alternates & VE, allowances and unit prices travel with the version
            CREATE TEMP TABLE mpo ON COMMIT DROP AS SELECT OptionId AS old, nextval(pg_get_serial_sequence('gridline.priceoption','optionid')) AS new FROM PriceOption WHERE VersionId = @from;
            INSERT INTO PriceOption (OptionId, VersionId, Kind, OptionNo, OptionIndex, Description, Status, SortKey, QuotedPct, Comments, IncludeCodes, ChangedBy) OVERRIDING SYSTEM VALUE
            SELECT mpo.new, @to, o.Kind, o.OptionNo, o.OptionIndex, o.Description, o.Status, o.SortKey, o.QuotedPct, o.Comments, o.IncludeCodes, @u FROM PriceOption o JOIN mpo ON mpo.old = o.OptionId;
            INSERT INTO PriceOptionTrade (OptionId, TradeId, CarriedAmount, Note)
            SELECT mpo.new, mt.new, x.CarriedAmount, x.Note FROM PriceOptionTrade x JOIN mpo ON mpo.old = x.OptionId JOIN mt ON mt.old = x.TradeId;
            CREATE TEMP TABLE mps ON COMMIT DROP AS SELECT SumId AS old, nextval(pg_get_serial_sequence('gridline.provisionalsum','sumid')) AS new FROM ProvisionalSum WHERE VersionId = @from;
            INSERT INTO ProvisionalSum (SumId, VersionId, TradeId, Kind, Description, Amount, SortKey, SumIndex, ChangedBy) OVERRIDING SYSTEM VALUE
            SELECT mps.new, @to, mt.new, p.Kind, p.Description, p.Amount, p.SortKey, p.SumIndex, @u FROM ProvisionalSum p JOIN mps ON mps.old = p.SumId LEFT JOIN mt ON mt.old = p.TradeId;
            INSERT INTO ProvisionalSumTrade (SumId, TradeId, Included)
            SELECT mps.new, mt.new, x.Included FROM ProvisionalSumTrade x JOIN mps ON mps.old = x.SumId JOIN mt ON mt.old = x.TradeId;
            UPDATE ScopeLine n SET SumId = mps.new FROM ml JOIN ScopeLine o ON o.LineId = ml.old JOIN mps ON mps.old = o.SumId WHERE n.LineId = ml.new;
            CREATE TEMP TABLE mur ON COMMIT DROP AS SELECT UnitRateId AS old, nextval(pg_get_serial_sequence('gridline.unitrate','unitrateid')) AS new FROM UnitRate WHERE VersionId = @from;
            INSERT INTO UnitRate (UnitRateId, VersionId, UnitIndex, Description, Unit, QuotedPct, Comments, IncludeCodes, ChangedBy) OVERRIDING SYSTEM VALUE
            SELECT mur.new, @to, r.UnitIndex, r.Description, r.Unit, r.QuotedPct, r.Comments, r.IncludeCodes, @u FROM UnitRate r JOIN mur ON mur.old = r.UnitRateId;
            INSERT INTO UnitRateTrade (UnitRateId, TradeId, Price, Note, IsSelected)
            SELECT mur.new, mt.new, x.Price, x.Note, x.IsSelected FROM UnitRateTrade x JOIN mur ON mur.old = x.UnitRateId JOIN mt ON mt.old = x.TradeId;

            UPDATE ProjectVersion d SET GrossArea = s.GrossArea, ZonesSetArea = s.ZonesSetArea, PrimaryAreaUnit = s.PrimaryAreaUnit, PrimaryUnitLabel = s.PrimaryUnitLabel, TotalUnits = s.TotalUnits, ApplyFeeExcludes = s.ApplyFeeExcludes, FeeExcludes = s.FeeExcludes, ExcludeGenreqFromDirect = s.ExcludeGenreqFromDirect, ReservePrecon = s.ReservePrecon, ReserveGenconds = s.ReserveGenconds, PreconQuotedPct = s.PreconQuotedPct, GenreqQuotedPct = s.GenreqQuotedPct, BuildMonths = s.BuildMonths, FeePct = s.FeePct, EditionId = s.EditionId,
                   RequirementSetId = s.RequirementSetId, BaselineName = CASE WHEN d.ProjectId = s.ProjectId THEN s.BaselineName ELSE NULL END
            FROM ProjectVersion s WHERE s.VersionId = @from AND d.VersionId = @to;
            """, p);
    }
}

public static class PreconSetup
{
    /// <summary>A new version gets the default precon tasks as columns (was: Tasks with DefaultInclude).</summary>
    public static Task DefaultTasks(Work w, long versionId) => w.Exec("""
        INSERT INTO VersionTask (VersionId, ActivityId, SortOrder)
        SELECT @versionId, ActivityId, SortOrder FROM Activity WHERE Phase = 'precon' AND IsDefault AND ProjectId IS NULL AND IsActive
          AND CompanyId = (SELECT p.CompanyId FROM ProjectVersion v JOIN Project p ON p.ProjectId = v.ProjectId WHERE v.VersionId = @versionId)
        ON CONFLICT DO NOTHING
        """, new { versionId });
}

public static class Budgets
{
    /// <summary>Sets every trade's budget from the version's chosen budget column (BaselineName).</summary>
    public static async Task ApplyBaseline(Work w, long versionId) => await w.Exec("""
        UPDATE Trade t SET BudgetValue = bv.Amount
        FROM ProjectVersion v JOIN BudgetColumn bc ON bc.ProjectId = v.ProjectId AND bc.BudgetName = v.BaselineName
        JOIN BudgetValue bv ON bv.BudgetColumnId = bc.BudgetColumnId
        WHERE v.VersionId = @versionId AND t.VersionId = v.VersionId AND bv.CsiCode = t.CsiCode AND t.BudgetValue IS DISTINCT FROM bv.Amount
        """, new { versionId });
}

public static class ScopeSetup
{
    /// <summary>A new trade gets the version's base requirements (text locked) and one line per zone.</summary>
    public static async Task AddStandardLines(Work w, long tradeId)
    {
        await w.Exec("""
            INSERT INTO ScopeLine (TradeId, Section, RequirementLineId, SortKey, IsHeading, LineText, Desired, ChangedBy)
            SELECT t.TradeId, 'baserequirement', r.RequirementLineId, r.SortKey, r.IsHeading, r.LineText, r.Desired, @u
            FROM Trade t JOIN ProjectVersion v ON v.VersionId = t.VersionId JOIN RequirementLine r ON r.RequirementSetId = v.RequirementSetId
            WHERE t.TradeId = @tradeId;
            INSERT INTO ScopeLine (TradeId, Section, ZoneId, SortKey, LineText, ChangedBy)
            SELECT t.TradeId, 'zone', z.ZoneId, z.SortOrder, z.ZoneName, @u FROM Trade t JOIN Zone z ON z.VersionId = t.VersionId WHERE t.TradeId = @tradeId;
            """, new { tradeId, u = w.User.UserId });
        await Gridline.Calc.PricingExtras.AddAllowanceLines(w, tradeId);
    }

    public static Task AddZoneLineToTrades(Work w, long versionId, long zoneId) => w.Exec("""
        INSERT INTO ScopeLine (TradeId, Section, ZoneId, SortKey, LineText, ChangedBy)
        SELECT t.TradeId, 'zone', z.ZoneId, z.SortOrder, z.ZoneName, @u FROM Trade t JOIN Zone z ON z.ZoneId = @zoneId
        WHERE t.VersionId = @versionId AND t.ArchivedAt IS NULL
        """, new { versionId, zoneId, u = w.User.UserId });

    /// <summary>Applies the version's zones' default split to a trade that has none.</summary>
    public static Task DefaultZoneSplit(Work w, long tradeId) => w.Exec("""
        INSERT INTO TradeZone (TradeId, ZoneId, Share)
        SELECT t.TradeId, z.ZoneId, z.DefaultShare FROM Trade t JOIN Zone z ON z.VersionId = t.VersionId
        WHERE t.TradeId = @tradeId AND z.DefaultShare > 0 AND NOT EXISTS (SELECT 1 FROM TradeZone x WHERE x.TradeId = t.TradeId)
        """, new { tradeId });
}
