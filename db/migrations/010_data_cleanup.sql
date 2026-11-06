-- Gridline v2 · migration 010 · data clean-up after the first hosted release
--  * text that was double-encoded on the way through a Windows console in an earlier backup ("Â·" for "·")
--  * every company gets a sign-in handle and brand colours (already true for fresh databases)
SET search_path = gridline, public;

CREATE OR REPLACE FUNCTION FixMojibake(t text) RETURNS text LANGUAGE sql IMMUTABLE AS $$
  SELECT replace(replace(replace(replace(t, E'Â·', E'·'), E'â\u0080\u0093', E'–'), E'â\u0080\u0094', E'—'), E'Â ', ' ')
$$;
UPDATE ProjectVersion SET BaselineName = FixMojibake(BaselineName) WHERE BaselineName LIKE E'%Â%' OR BaselineName LIKE E'%â\u0080%';
UPDATE ProjectVersion SET VersionLabel = FixMojibake(VersionLabel) WHERE VersionLabel LIKE E'%Â%' OR VersionLabel LIKE E'%â\u0080%';
UPDATE BudgetColumn SET BudgetName = FixMojibake(BudgetName) WHERE BudgetName LIKE E'%Â%' OR BudgetName LIKE E'%â\u0080%';
UPDATE Trade SET TradeName = FixMojibake(TradeName), ClientNote = FixMojibake(ClientNote), TeamNote = FixMojibake(TeamNote)
WHERE TradeName LIKE E'%Â%' OR ClientNote LIKE E'%Â%' OR TeamNote LIKE E'%Â%' OR TradeName LIKE E'%â\u0080%' OR ClientNote LIKE E'%â\u0080%' OR TeamNote LIKE E'%â\u0080%';
UPDATE ScopeLine SET LineText = FixMojibake(LineText), LineNote = FixMojibake(LineNote) WHERE LineText LIKE E'%Â%' OR LineNote LIKE E'%Â%' OR LineText LIKE E'%â\u0080%' OR LineNote LIKE E'%â\u0080%';
UPDATE ScopeAnswer SET AnswerText = FixMojibake(AnswerText), Note = FixMojibake(Note) WHERE AnswerText LIKE E'%Â%' OR Note LIKE E'%Â%' OR AnswerText LIKE E'%â\u0080%' OR Note LIKE E'%â\u0080%';
UPDATE CostLine SET ItemName = FixMojibake(ItemName), CategoryName = FixMojibake(CategoryName), Note = FixMojibake(Note) WHERE ItemName LIKE E'%Â%' OR CategoryName LIKE E'%Â%' OR Note LIKE E'%Â%' OR ItemName LIKE E'%â\u0080%' OR Note LIKE E'%â\u0080%';
UPDATE Qualification SET QualText = FixMojibake(QualText) WHERE QualText LIKE E'%Â%' OR QualText LIKE E'%â\u0080%';
UPDATE TradePartner SET CompanyName = FixMojibake(CompanyName) WHERE CompanyName LIKE E'%Â%' OR CompanyName LIKE E'%â\u0080%';
UPDATE Project SET ProjectName = FixMojibake(ProjectName), ClientName = FixMojibake(ClientName) WHERE ProjectName LIKE E'%Â%' OR ClientName LIKE E'%Â%' OR ProjectName LIKE E'%â\u0080%' OR ClientName LIKE E'%â\u0080%';

INSERT INTO SchemaChange (ChangeNo, ChangeName) VALUES (10, 'Data clean-up: double-encoded text from earlier backups') ON CONFLICT (ChangeNo) DO NOTHING;
