-- Gridline v2 · migration 011 · the rest of the double-encoded text (cp1252 flavour: "â€“" for "–", "â€™" for "’" …)
-- found in template names and lines after the starter library was cloned.
SET search_path = gridline, public;

CREATE OR REPLACE FUNCTION FixMojibake(t text) RETURNS text LANGUAGE sql IMMUTABLE AS $$
  SELECT replace(replace(replace(replace(replace(replace(replace(replace(replace(replace(replace(t,
    E'â€“', E'–'),   -- –
    E'â€”', E'—'),   -- —
    E'â€™', E'’'),   -- ’
    E'â€œ', E'“'),   -- “
    E'â€\u009d', E'”'),   -- ”
    E'â€¢', E'•'),   -- •
    E'â\u0080\u0093', E'–'), E'â\u0080\u0094', E'—'), E'â\u0080\u0099', E'’'),   -- latin-1 flavour
    E'Â·', E'·'), E'Â ', ' ')
$$;
DO $$ DECLARE r record; BEGIN
  FOR r IN SELECT * FROM (VALUES
    ('ScopeKit','KitName'), ('ScopeKitLine','LineText'), ('CostKit','KitName'), ('CostKitItem','ItemName'), ('CostKitItem','CategoryName'),
    ('RequirementSet','SetName'), ('RequirementLine','LineText'), ('RequirementLine','SectionName'), ('QualificationSet','SetName'), ('QualificationSetLine','QualText'),
    ('Activity','ActivityName'), ('Trade','TradeName'), ('Trade','ClientNote'), ('Trade','TeamNote'), ('ScopeLine','LineText'), ('ScopeLine','LineNote'),
    ('ScopeAnswer','AnswerText'), ('ScopeAnswer','Note'), ('CostLine','ItemName'), ('CostLine','CategoryName'), ('CostLine','Note'), ('Qualification','QualText'),
    ('TradePartner','CompanyName'), ('PartnerPerson','FullName'), ('Project','ProjectName'), ('Project','ClientName'), ('ProjectVersion','VersionLabel'), ('ProjectVersion','BaselineName'),
    ('Zone','ZoneName'), ('IndirectItem','ItemName'), ('IndirectItem','Description'), ('StaffRole','RoleName'), ('Staff','FullName'), ('EstimateLine','LineText')) v(t, c)
  LOOP
    EXECUTE format('UPDATE gridline.%s SET %s = FixMojibake(%s) WHERE %s ~ E''\\u00e2'' OR %s ~ E''\\u00c2''', r.t, r.c, r.c, r.c, r.c);
  END LOOP;
END $$;

INSERT INTO SchemaChange (ChangeNo, ChangeName) VALUES (11, 'Template and line text clean-up (cp1252 double encoding)') ON CONFLICT (ChangeNo) DO NOTHING;
