-- Gridline v2 · migration 003 · indexes the API's lookups and the bulk import rely on
SET search_path = gridline, public;
CREATE INDEX IF NOT EXISTS IxScopeLineTradeAll   ON ScopeLine (TradeId, Section);
CREATE INDEX IF NOT EXISTS IxPartnerPersonPartner ON PartnerPerson (PartnerId);
CREATE INDEX IF NOT EXISTS IxRequirementLineSet  ON RequirementLine (RequirementSetId, SortKey);
CREATE INDEX IF NOT EXISTS IxTeamMemberProject   ON TeamMember (ProjectId);
CREATE INDEX IF NOT EXISTS IxTeamMemberWeekWeek  ON TeamMemberWeek (WeekId);
CREATE INDEX IF NOT EXISTS IxTradeZoneZone       ON TradeZone (ZoneId);
CREATE INDEX IF NOT EXISTS IxBidPartner          ON Bid (PartnerId);
CREATE INDEX IF NOT EXISTS IxInviteTrade         ON BidInvite (TradeId);
CREATE INDEX IF NOT EXISTS IxReadingBid          ON ProposalReading (BidId);
CREATE INDEX IF NOT EXISTS IxSuggestionReading   ON ProposalSuggestion (ReadingId);
CREATE INDEX IF NOT EXISTS IxLoginTokenUser      ON LoginToken (UserId);
CREATE INDEX IF NOT EXISTS IxRecentProjectUser   ON RecentProject (UserId, OpenedAt DESC);
CREATE INDEX IF NOT EXISTS IxPresenceUser        ON Presence (UserId);
INSERT INTO SchemaChange (ChangeNo, ChangeName) VALUES (3, 'Indexes for API lookups and bulk import') ON CONFLICT (ChangeNo) DO NOTHING;
