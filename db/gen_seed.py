"""Writes 002_seed.sql: dummy data for Gridline v2 (one schema: gridline)."""
import random, datetime as dt, os
random.seed(11)
out = []
def q(v):
    if v is None: return 'NULL'
    if isinstance(v, bool): return 'true' if v else 'false'
    if isinstance(v, (int, float)): return repr(v) if isinstance(v, float) else str(v)
    if isinstance(v, (dt.date, dt.datetime)): return "'" + v.isoformat() + "'"
    if isinstance(v, list): return "'{" + ",".join(str(x) for x in v) + "}'"
    return "'" + str(v).replace("'", "''") + "'"
def ins(table, cols, rows):
    if not rows: return
    val = lambda x: x[1:] if isinstance(x, str) and x.startswith('§') else q(x)
    for k in range(0, len(rows), 500):
        chunk = rows[k:k+500]
        out.append(f"INSERT INTO {table} ({', '.join(cols)}) VALUES\n" + ",\n".join("(" + ", ".join(val(x) for x in r) + ")" for r in chunk) + ";")
S = lambda sql: '§(' + sql + ')'
U = lambda e: S(f"SELECT UserId FROM AppUser WHERE LoginEmail LIKE '{e}@%'")
out.append("BEGIN;\nSET client_min_messages = warning;\nSET search_path = gridline, public;")

# ---------------- company ----------------
ins('CompanyProfile', ['DisplayName','LegalName','HelpEmail'], [('Northshore Builders','Northshore Builders LLC','help@northshore.example')])
divs = [('HC','Healthcare'),('IND','Industrial'),('COM','Commercial'),('EDU','Education'),('PUB','Public works'),('DC','Data centers')]
ins('Division', ['ShortCode','DivisionName','CrmAlias'], [(a, b, b.upper()) for a, b in divs])
ins('Branch', ['BranchName','RegionName','Street','City','StateCode','PostalCode'], [('Falls Church HQ','Mid-Atlantic','2900 Fairview Park Dr','Falls Church','VA','22042'),
    ('Richmond','Mid-Atlantic','100 E Main St','Richmond','VA','23219'),('Raleigh','Southeast','400 Fayetteville St','Raleigh','NC','27601'),('Austin','Central','500 Congress Ave','Austin','TX','78701')])
users = [('Karthik','Raman','k.raman','#6A32D4','admin'),('Alice','Chen','alice.chen','#0ea5e9','estimator'),('Mia','Diaz','mia.diaz','#e11d74','estimator'),
         ('Luis','Ortiz','luis.ortiz','#16a34a','manager'),('Sam','Patel','sam.patel','#f59e0b','estimator'),('Rhea','King','rhea.king','#8b5cf6','viewer'),('Demo','User','demo','#ef4444','admin')]
ins('AppUser', ['GivenName','FamilyName','LoginEmail','Colour','HomeDivisionId'], [(a, b, c + '@northshore.example', d, 1) for a, b, c, d, _ in users])
roles = [('admin','Administrator','Everything, including company settings'),('estimator','Estimator','Edit projects they are on'),
         ('manager','Pre-construction manager','Edit, approve and award'),('viewer','Read only','View projects they are on')]
ins('AccessRole', ['RoleCode','RoleName','Summary'], roles)
caps = {'admin': ['*'], 'estimator': ['project.edit','trade.carry','scope.edit','import.run','export.run','invite.send','proposal.read'],
        'manager': ['project.edit','trade.carry','scope.edit','import.run','export.run','invite.send','proposal.read','version.lock'], 'viewer': ['project.view','export.run']}
ins('RoleGrant', ['RoleId','Capability'], [(S(f"SELECT RoleId FROM AccessRole WHERE RoleCode='{r}'"), c) for r, cs in caps.items() for c in cs])
ins('UserRole', ['UserId','RoleId'], [(U(c), S(f"SELECT RoleId FROM AccessRole WHERE RoleCode='{r}'")) for _, _, c, _, r in users])
roles_s = [('Project executive','PX',6,185),('Project manager','PM',5,145),('Superintendent','SUPT',5,138),('Project engineer','PE',3,92),
           ('Estimator','EST',4,120),('Pre-construction manager','PCM',5,160),('Safety manager','SAFE',4,110),('MEP coordinator','MEP',4,118)]
ins('StaffRole', ['RoleName','ShortName','Seniority','DefaultRate','OpenToAllProjects'], [(a, b, c, d, a == 'Estimator') for a, b, c, d in roles_s])
first = ['Ava','Ben','Cara','Dev','Eli','Fay','Gus','Hana','Ivan','Jade','Kai','Lena','Milo','Nia','Owen','Pia','Quinn','Ravi','Sara','Theo']
last = ['Adams','Brooks','Cole','Dunn','Ellis','Ford','Grant','Hayes','Irwin','Jensen','Khan','Lopez','Moore','Nash','Ortega','Price']
staff = [('Luis Ortiz','Project manager'),('Rhea King','Superintendent'),('Alice Chen','Estimator'),('Sam Patel','Estimator')]
staff += [(f"{first[i % 20]} {last[(i * 7) % 16]}", roles_s[i % 8][0]) for i in range(36)]
ins('Staff', ['StaffRoleId','DivisionId','BranchId','StaffNo','FullName','WorkEmail','HourlyCost','BillRate'],
    [(S(f"SELECT StaffRoleId FROM StaffRole WHERE RoleName='{r}'"), 1 + i % 6, 1 + i % 4, f"E{10400 + i}", n, f"{n.lower().replace(' ', '.')}.{i}@northshore.example",
      round([x[3] for x in roles_s if x[0] == r][0] * .55, 2), [x[3] for x in roles_s if x[0] == r][0]) for i, (n, r) in enumerate(staff)])
ins('Setting', ['SettingKey','SettingName','Note','SettingValue'], [
    ('calc.checkOnServer','Check browser totals on the server','Every save is recalculated for the changed trade','true'),
    ('presence.timeoutSeconds','Presence timeout','Seconds without a heartbeat before someone drops off','45'),
    ('import.maxRowsPerSheet','Import row limit','Largest sheet the Excel import accepts','200000'),
    ('portal.inviteDays','Bid link lifetime','Days a sub can use a bid link','21'),
    ('ai.proposalModel','AI model for the proposal reader','Set the API key in the server settings, not here','"gpt-5.6-terra"')])

# ---------------- reference ----------------
ins('UsState', ['StateCode','StateName','SalesTaxRate','CoverRate'], [('VA','Virginia',.006,.18),('NC','North Carolina',.0475,.17),('MD','Maryland',.06,.19),
    ('DC','District of Columbia',.06,.21),('TX','Texas',0.0,.24),('CO','Colorado',.029,.16),('GA','Georgia',.04,.19),('FL','Florida',.06,.30)])
ins('County', ['StateCode','CountyName'], [('VA','Arlington'),('VA','Fairfax'),('VA','Henrico'),('VA','Loudoun'),('NC','Wake'),('MD','Montgomery'),('TX','Travis'),('CO','Denver'),('GA','Fulton'),('FL','Miami-Dade')])
ins('CodeEdition', ['EditionName','EditionYear','IsCurrent'], [('MasterFormat 2018',2018,True)])
exec(open('/tmp/groups_block.py').read())
ins('CodeGroup', ['EditionId','GroupNo','GroupTitle'], [(1, g, t) for g, t in groups])
exec(open('/tmp/codes_block.py').read())
codes += [('07 42 00','Insulated Metal Panels'),('08 33 00','Overhead Coiling Doors'),('09 90 00','Painting')] if not any(c == '07 42 00' for c, _ in codes) else []
ins('WorkCode', ['GroupId','CsiCode','CodeTitle'], [(S(f"SELECT GroupId FROM CodeGroup WHERE GroupNo='{c[:2]}'"), c, t) for c, t in codes])
ins('UnitOfMeasure', ['UomCode','UomName'], [('EA','Each'),('LF','Linear foot'),('SF','Square foot'),('CY','Cubic yard'),('LS','Lump sum'),('HR','Hour'),('DAY','Day'),('WK','Week'),('MO','Month'),('TON','Ton')])
MK = [('genreq','General requirements',10,'direct',0),('precon','Preconstruction',20,'subtotal',0),('reserveproject','Project reserve',30,'subtotal',.02),
      ('reservedesign','Design reserve',40,'subtotal',.015),('reserveescalation','Escalation reserve',50,'subtotal',.01),('bond','P & P bond',60,'contract',.0075),
      ('cover','Builders risk',70,'contract',.0018),('liability','L & P insurance',80,'contract',.011),('fee','Fee',90,'contract',.045),('salestax','Retail sales tax',100,'tax',.006)]
ins('MarkupType', ['MarkupCode','MarkupName','SortOrder','AppliesTo','DefaultRatePct'], MK)
ins('BondBand', ['FromAmount','ToAmount','RatePerThousand'], [(0,100000,25),(100000,500000,15),(500000,2500000,10),(2500000,5000000,7.5),(5000000,None,6.5)])
ins('TravelRate', ['City','StateCode','FiscalYear','LodgingDaily','MealsDaily'], [(c, s, 2027, l, m) for c, s, l, m in
    [('Arlington','VA',258,79),('Richmond','VA',151,69),('Raleigh','NC',148,69),('Austin','TX',199,74),('Denver','CO',211,79)]])
acts = [('precon','Kick-off and estimate plan'),('precon','Scope packages'),('precon','Bid leveling meetings'),('build','Mobilization'),('build','Weekly OAC meetings'),('closeout','Punch list'),('closeout','Turnover documents')]
ins('Activity', ['Phase','ActivityName','SortOrder'], [(a, b, i + 1) for i, (a, b) in enumerate(acts)])

# ---------------- templates ----------------
ins('RequirementSet', ['SetName','IsTemplate','DivisionId'], [('Company standard requirements',True,None),('Riverside Medical – base requirements',False,1)])
REQ = [('General terms',True,None),('Furnish all labor, materials, equipment and supervision for a complete system per the specifications.',False,'included'),
       ('Acknowledge site logistics plan, crane schedule and hoisting by GC.',False,'included'),('Offsite parking and shuttling for all personnel.',False,'included'),
       ('Insurance & bonding',True,None),('Payment and performance bond at 100% of subcontract value.',False,'included'),('Builders risk deductible responsibility up to $25,000.',False,'excluded'),
       ('Cleanup',True,None),('Daily cleanup and debris removal to GC dumpster.',False,'included')]
ins('RequirementLine', ['RequirementSetId','SortKey','SectionName','IsHeading','LineText','Desired'], [(s, i + 1, 'General', h, b, d) for s in (1, 2) for i, (b, h, d) in enumerate(REQ)])
ins('ScopeKit', ['CodeId','KitName'], [(S("SELECT CodeId FROM WorkCode WHERE CsiCode='08 44 00'"),'Curtain wall – unitized'),(S("SELECT CodeId FROM WorkCode WHERE CsiCode='03 30 00'"),'Concrete – slab on grade')])
ins('ScopeKitLine', ['KitId','SortKey','IsHeading','LineText','Desired'], [(1,1,True,'System',None),(1,2,False,'Unitized curtain wall, including visual mock-up.','included'),
    (1,3,False,'Performance mock-up and AAMA testing.','included'),(1,4,True,'Site conditions',None),(1,5,False,'Winter protection and temporary enclosures.','included'),
    (2,1,False,'Slab on grade with vapor barrier and finishing.','included'),(2,2,False,'Joint sealing at saw cuts.','included')])
PRECON_KIT = [('Team','Pre-construction manager',320,'HR',160),('Team','Estimators',960,'HR',120),('Team','MEP coordinator',240,'HR',118),
              ('Expenses','Plan printing & reproduction',1,'LS',6500),('Expenses','Travel to site visits',12,'EA',650),('Expenses','Estimating software licences',4,'MO',900)]
GENREQ_KIT = [('Facilities','Office trailer 12x60',24,'MO',1450),('Facilities','Toilets',24,'MO',380),('Facilities','Temporary power',24,'MO',2200),
              ('IT & communications','Internet & phones',24,'MO',900),('Protection','Floor protection',98000,'SF',.65),('Protection','Temporary barricades',1,'LS',38000),
              ('Cleaning','Final clean allowance',1,'LS',22000),('Safety','Safety supplies & signage',24,'MO',1100)]
ins('CostKit', ['KitName','Area','IsDefault'], [('Pre-construction standard','precon',True),('General requirements – large project','genreq',True)])
ins('CostKitItem', ['CostKitId','CategoryName','ItemName','DefaultQty','UomCode','DefaultRate','SortOrder'],
    [(1, a, b, c, d, e, i + 1) for i, (a, b, c, d, e) in enumerate(PRECON_KIT)] + [(2, a, b, c, d, e, i + 1) for i, (a, b, c, d, e) in enumerate(GENREQ_KIT)])

# ---------------- partners ----------------
exec(open('/tmp/trades_block.py').read())   # TRADES, STATE, USER_ADDED, ADJ
names = sorted({b[0] for t in TRADES for b in t[2]})
filler = [f"{a} {b}" for a in ['Atlas','Beacon','Cobalt','Delta','Evergreen','Frontier','Harbor','Ironwood','Juniper','Lakeside','Monarch','Northstar','Oakridge','Pinnacle','Sterling','Titan']
          for b in ['Builders','Mechanical','Electric','Interiors']]
allp = names + [f for f in filler if f not in names]
ins('TradePartner', ['CompanyName','TaxRef','Website','IsPrequalified','PrequalExpires','SingleLimit','AggregateLimit','DiverseTags','AddedByUser'],
    [(n, f"{random.randint(10, 99)}-{random.randint(1000000, 9999999)}", f"https://{''.join(ch for ch in n.lower() if ch.isalnum())}.example", random.random() < .75,
      dt.date(2027, random.randint(1, 12), 1), random.choice([2, 5, 10, 25]) * 1000000, random.choice([10, 20, 50]) * 1000000,
      random.choice([['MBE'], ['WBE'], ['SDVOSB'], [], [], []]), n in USER_ADDED) for n in allp])
P = lambda n: S("SELECT PartnerId FROM TradePartner WHERE CompanyName=" + q(n))
ins('PartnerBranch', ['PartnerId','BranchName','Street','City','StateCode','PostalCode','Phone','IsHeadOffice'],
    [(P(n), 'Head office', f"{random.randint(100, 9999)} Commerce Way", random.choice(['Richmond','Arlington','Raleigh','Baltimore']), random.choice(['VA','NC','MD']),
      f"2{random.randint(1000, 9999)}", f"(804) 555-{random.randint(1000, 9999)}", True) for n in allp])
def mail(n, k): return f"{['estimating','pm'][k]}@{''.join(ch for ch in n.lower() if ch.isalnum())[:18]}.example"
ins('PartnerPerson', ['PartnerId','FullName','JobTitle','Email','Phone','IsPrimary'],
    [(P(n), f"{random.choice(first)} {random.choice(last)}", t, mail(n, k), f"(804) 555-{random.randint(1000, 9999)}", k == 0) for n in allp for k, t in enumerate(['Chief estimator','Project manager'])])
ins('PartnerCode', ['PartnerId','CodeId'], sorted({(P(b[0]), S(f"SELECT CodeId FROM WorkCode WHERE CsiCode='{t[0]}'")) for t in TRADES for b in t[2]}))

# ---------------- projects ----------------
today = dt.date(2026, 11, 3)
projects = [  # no, name, stage, client, div, type, contract, city, state, county, due, months, gsf, crm, est, pm
 ('EST-24031','Riverside Medical Pavilion','bidding','Riverside Health System','HC','Hospital','GMP','Arlington','VA','Arlington',dt.date(2026,12,18),26,508000,112000000,'alice.chen','luis.ortiz'),
 ('EST-24044','Northgate Logistics Hub','pursuit','Northgate Partners LLC','IND','Warehouse','GMP','Richmond','VA','Henrico',dt.date(2027,1,22),14,412000,36400000,'sam.patel','luis.ortiz'),
 ('EST-24050','Harbor View Office Tower','lead','Harbor View Holdings','COM','Office','GMP','Raleigh','NC','Wake',dt.date(2027,3,12),30,610000,184000000,'mia.diaz','luis.ortiz')]
past = [('EST-23710','Westbrook Clinic Expansion','awarded','Westbrook Health','HC','Hospital','GMP','Richmond','VA','Henrico',dt.date(2025,9,12),20,214000,61000000,'alice.chen','luis.ortiz',.42),
        ('EST-23744','Lakeside Medical Office','submitted','Lakeside Physicians','HC','Hospital','Lump sum','Raleigh','NC','Wake',dt.date(2026,2,20),16,168000,44000000,'mia.diaz','luis.ortiz',.33),
        ('EST-23802','Pinecrest Children\'s Wing','lost','Pinecrest Health','HC','Hospital','GMP','Fairfax','VA','Fairfax',dt.date(2026,4,8),24,302000,98000000,'alice.chen','luis.ortiz',.6),
        ('EST-23815','Summit Regional Hospital Tower','awarded','Summit Health','HC','Hospital','GMP','Austin','TX','Travis',dt.date(2026,6,30),30,640000,231000000,'sam.patel','luis.ortiz',1.25),
        ('EST-23830','Cedar Park Middle School','awarded','Cedar Park ISD','EDU','School','Lump sum','Austin','TX','Travis',dt.date(2026,7,30),18,186000,48200000,'mia.diaz','luis.ortiz',.36)]
def br(city): return S(f"SELECT BranchId FROM Branch WHERE City='{city}'") if city in ('Richmond','Raleigh','Austin') else 1
allproj = [p[:16] for p in projects] + [p[:16] for p in past]
ins('Project', ['ProjectNo','ProjectName','Stage','ClientName','DivisionId','ProjectType','ContractType','City','StateCode','CountyName','BidDueOn','CrmRef','CrmValue','EstimatorId','ManagerId','BranchId','CreatedBy'],
    [(a, b, c, d, S(f"SELECT DivisionId FROM Division WHERE ShortCode='{e}'"), f, g, h, i, j, k, 'OPP-' + a[4:], n, U(o), U(pm), br(h), U(o))
     for a, b, c, d, e, f, g, h, i, j, k, l, m, n, o, pm in allproj])
J = lambda no: S(f"SELECT ProjectId FROM Project WHERE ProjectNo='{no}'")
V = lambda no, v: S(f"SELECT VersionId FROM ProjectVersion pv JOIN Project p USING (ProjectId) WHERE p.ProjectNo='{no}' AND pv.VersionNo={v}")
vers = [('EST-24031',1,'Budget pricing',False,'active',None),('EST-24031',2,'GMP bid',True,'active','Owner budget · Sep 30'),('EST-24044',1,'Concept',True,'draft',None),('EST-24050',1,'Initial',True,'draft',None)]
vers += [(p[0], 1, 'Final', True, 'submitted', 'Owner budget') for p in past]
meta = {p[0]: p for p in allproj}
ins('ProjectVersion', ['ProjectId','VersionNo','VersionLabel','IsPrimary','Status','EditionId','RequirementSetId','GrossArea','BuildMonths','FeePct','BaselineName','ReadinessPct','CreatedBy','CopiedFromVersionId'],
    [(J(a), b, c, d, st, 1, 2 if a == 'EST-24031' else 1, meta[a][12], meta[a][11], 4.5, bl, random.choice([35, 55, 78]) if a != 'EST-24031' else 82, U(meta[a][14]), V('EST-24031', 1) if (a, b) == ('EST-24031', 2) else None)
     for a, b, c, d, st, bl in vers])
ins('ProjectMember', ['ProjectId','UserId','Access'], [(J(p[0]), U(u), 'owner' if u in ('demo', 'k.raman', p[14]) else 'viewer' if u == 'rhea.king' else 'editor') for p in allproj for _, _, u, _, _ in users])
ins('RecentProject', ['UserId','ProjectId','IsFavourite'], [(U('demo'), J(p[0]), i == 0) for i, p in enumerate(projects)])
ZONES = {'EST-24031': [('CS','Core & Shell',286000,'#3341c9',.62),('CF','Clinic Fit-out',98000,'#7a5cf0',.21),('PD','Parking Deck',124000,'#f2a516',.12),('SW','Sitework',0,'#11806b',.05)],
         'EST-24044': [('WH','Warehouse',360000,'#3341c9',.8),('OFF','Office mezzanine',22000,'#7a5cf0',.12),('SITE','Sitework',30000,'#11806b',.08)],
         'EST-24050': [('TWR','Tower',560000,'#3341c9',.85),('POD','Podium',50000,'#7a5cf0',.15)]}
zrows = []
for no, zs in ZONES.items():
    for v in ((1, 2) if no == 'EST-24031' else (1,)):
        zrows += [(V(no, v), c, n, i + 1, sf, sh, col) for i, (c, n, sf, col, sh) in enumerate(zs)]
for p in past: zrows.append((V(p[0], 1), 'ALL', 'Whole building', 1, p[12], 1.0, '#3341c9'))
ins('Zone', ['VersionId','ZoneCode','ZoneName','SortOrder','FloorArea','DefaultShare','Colour'], zrows)
mrows = []
for a, b, *_ in vers:
    for code, name, so, ap, rate in MK:
        if code in ('genreq', 'precon'): continue
        mrows.append((V(a, b), code, rate * 100))
ins('VersionMarkup', ['VersionId','MarkupCode','RatePct'], mrows)

# ---------------- trades & bids ----------------
ZMAP = {'core': 'CS', 'clin': 'CF', 'park': 'PD', 'site': 'SW'}
trows, brows, tzrows = [], [], []
def add_trades(no, vno, scale=1.0, jitter=0.0, est='alice.chen'):
    for i, (code, title, bids, split) in enumerate(TRADES):
        f = scale * (1 + random.uniform(-jitter, jitter))
        bb = [(n, None if a is None else round(a * f, -2)) for n, a in bids]
        posted = [a for _, a in bb if a]
        low = min(posted)
        pick = 0 if (no == 'EST-24031' and i == 4) else (-1 if (no == 'EST-24031' and i == 16) else [a for _, a in bb].index(low))
        carried = None if pick < 0 else bb[pick][1] + round(ADJ[i] * f, -2)
        plug = 3500000 if (no == 'EST-24031' and i == 16) else None
        budget = round(low * random.uniform(.96, 1.06), -3)
        T = S(f"SELECT TradeId FROM Trade WHERE VersionId=({V(no, vno)[2:-1]}) AND CsiCode='{code}'")
        trows.append((V(no, vno), S(f"SELECT CodeId FROM WorkCode WHERE CsiCode='{code}'"), code, title, low, carried if carried is not None else plug, round(ADJ[i] * f, -2), budget,
                      None if carried is None else carried - budget, plug, 'Budgetary plug pending geotech report' if plug else ('Consolidated joint firestopping' if (no == 'EST-24031' and i == 4) else None),
                      'Two bids expected Nov 12' if plug else None, U(est), i + 1))
        for k, (n, a) in enumerate(bb):
            st = STATE.get(n, 'posted' if a else 'invited')
            brows.append((T, P(n), n, k + 1, st, a is not None, k == pick, a, 'manual', a))
        zc = [z for z in ZONES.get(no, [('ALL',)])]
        if no in ZONES:
            for zk, sh in split.items():
                tzrows.append((T, S(f"SELECT ZoneId FROM Zone WHERE VersionId=({V(no, vno)[2:-1]}) AND ZoneCode='{ZMAP.get(zk, zk)}'"), sh))
        else:
            tzrows.append((T, S(f"SELECT ZoneId FROM Zone WHERE VersionId=({V(no, vno)[2:-1]}) AND ZoneCode='ALL'"), 1.0))
add_trades('EST-24031', 2)
add_trades('EST-24031', 1, 1.03, .02)
for p in past: add_trades(p[0], 1, p[16], .12, p[14])
ins('Trade', ['VersionId','CodeId','CsiCode','TradeName','LowestValue','CarriedValue','Adjustment','BudgetValue','BudgetDelta','PlugAmount','ClientNote','TeamNote','EstimatorId','SortOrder'], trows)
ins('Bid', ['TradeId','PartnerId','BidderName','LaneNo','State','IsPosted','IsCarried','BaseAmount','BaseSource','TotalAmount'], brows)
ins('TradeZone', ['TradeId','ZoneId','Share'], tzrows)
# Northgate: a few trades so the project isn't empty
NG = [('03 30 00','Cast-in-place Concrete',6400000,{'WH':.75,'OFF':.15,'SITE':.1},[('Bayline Concrete',6210000),('Granite State Concrete',6390000)]),
      ('05 12 00','Structural Steel',8900000,{'WH':.85,'OFF':.15},[('Allied Steel Erectors',8850000),('Ironclad Fabricators',None)]),
      ('31 00 00','Earthwork',3150000,{'SITE':1},[('Terra Excavating',3090000)])]
for i, (code, name, bud, sp, bids) in enumerate(NG):
    T = S(f"SELECT TradeId FROM Trade WHERE VersionId=({V('EST-24044', 1)[2:-1]}) AND CsiCode='{code}'")
    posted = [a for _, a in bids if a]
    ins('Trade', ['VersionId','CodeId','CsiCode','TradeName','LowestValue','BudgetValue','EstimatorId','SortOrder'], [(V('EST-24044', 1), S(f"SELECT CodeId FROM WorkCode WHERE CsiCode='{code}'"), code, name, min(posted), bud, U('sam.patel'), i + 1)])
    ins('Bid', ['TradeId','PartnerId','BidderName','LaneNo','State','IsPosted','IsCarried','BaseAmount','BaseSource','TotalAmount'],
        [(T, P(n), n, k + 1, 'posted' if a else 'bidding', a is not None, False, a, 'import', a) for k, (n, a) in enumerate(bids)])
    ins('TradeZone', ['TradeId','ZoneId','Share'], [(T, S(f"SELECT ZoneId FROM Zone WHERE VersionId=({V('EST-24044', 1)[2:-1]}) AND ZoneCode='{z}'"), s) for z, s in sp.items()])

# ---------------- scope match (curtain wall, GMP version) ----------------
CW = S(f"SELECT TradeId FROM Trade WHERE VersionId=({V('EST-24031', 2)[2:-1]}) AND CsiCode='08 44 00'")
SC = [('baserequirement',True,'General terms',None,[]),
      ('baserequirement',False,'Furnish all labor, materials, equipment and supervision for a complete curtain wall system per sections 08 44 13 and 08 80 00.','included',['Included','Included','Included']),
      ('baserequirement',False,'Acknowledge site logistics plan, crane schedule and hoisting by GC.','included',['Included','Included','Excluded']),
      ('baserequirement',False,'Offsite parking and shuttling for all personnel.','included',['Included','Parking for 6 only','Included']),
      ('baserequirement',True,'Insurance & bonding',None,[]),
      ('baserequirement',False,'Payment and performance bond at 100% of subcontract value.','included',['Included','Excluded','']),
      ('tradeitem',True,'System',None,[]),
      ('tradeitem',False,'Unitized curtain wall, levels 2–7, including visual mock-up.','included',['Included','Included','Included']),
      ('tradeitem',False,'Performance mock-up and AAMA testing.','included',['Included','185000','Included']),
      ('tradeitem',False,'Sunshades and fins at south elevation.','included',['Included','Included','142000']),
      ('tradeitem',False,'Winter protection and temporary enclosures.','included',['Included','64000','98000']),
      ('tradeitem',False,'Field testing (AAMA 501.2) 3 locations.','included',['Included','',''])]
ins('ScopeLine', ['TradeId','Section','SortKey','IsHeading','LineText','Desired','LineNote','Indent'],
    [(CW, a, i + 1, h, t, d, 'Confirm count with Prism' if 'parking' in t else None, 1 if 'Sunshades' in t else 0) for i, (a, h, t, d, _) in enumerate(SC)])
ans = []
for i, (_, h, t, _, vals) in enumerate(SC):
    if h: continue
    L = S(f"SELECT LineId FROM ScopeLine WHERE TradeId=({CW[2:-1]}) AND SortKey={i + 1}")
    for k, v in enumerate(vals):
        B = S(f"SELECT BidId FROM Bid WHERE TradeId=({CW[2:-1]}) AND LaneNo={k + 1}")
        kind = 'blank' if v == '' else 'included' if v == 'Included' else 'excluded' if v == 'Excluded' else 'amount' if v.lstrip('-').isdigit() else 'text'
        ans.append((L, B, kind, int(v) if kind == 'amount' else None, v if kind == 'text' else None, 'red' if ('Winter' in t and k == 1) else None))
ins('ScopeAnswer', ['LineId','BidId','Kind','Amount','AnswerText','CellColour'], ans)
ins('EstimateLine', ['TradeId','SortKey','LineText','Qty','UomCode','UnitCost'], [(CW, 1, 'Unitized panels', 48200, 'SF', 152), (CW, 2, 'Sunshades', 1800, 'LF', 78)])
ins('IndirectItem', ['VersionId','ItemName','Amount','SortOrder'], [(V('EST-24031', 2), 'Owner IT & AV coordination', 250000, 1)])
ins('BudgetColumn', ['ProjectId','BudgetName'], [(J('EST-24031'), 'Target GMP')])
ins('BudgetValue', ['BudgetColumnId','CsiCode','Amount'], [(1, t[0], round(min(b[1] for b in t[2] if b[1]) * 1.02, -3)) for t in TRADES])

# ---------------- project team, precon, general requirements ----------------
start = dt.date(2026, 9, 7)
for no, months in (('EST-24031', 26), ('EST-24044', 14)):
    weeks = int(months * 4.33) + 16
    ins('ProjectWeek', ['ProjectId','WeekNo','StartsOn','Phase','MonthNo'],
        [(J(no), w + 1, start + dt.timedelta(weeks=w), 'precon' if w < 16 else ('closeout' if w >= weeks - 6 else 'build'), w // 4 + 1) for w in range(weeks)])
TEAM = [('Project executive',None,'Project executive'),('Project manager','Luis Ortiz','Project manager'),('Superintendent','Rhea King','Superintendent'),
        ('Project engineer',None,'Project engineer'),('Estimator','Alice Chen','Estimator'),('Pre-construction manager',None,'Pre-construction manager'),('Safety manager',None,'Safety manager')]
ins('TeamMember', ['ProjectId','StaffId','StaffRoleId','RoleLabel','SortKey','HourlyCost','BillRate','TravelRateId','TravelDays'],
    [(J('EST-24031'), S(f"SELECT StaffId FROM Staff WHERE FullName='{m}' LIMIT 1") if m else None, S(f"SELECT StaffRoleId FROM StaffRole WHERE RoleName='{r}'"), lab, i + 1,
      round([x[3] for x in roles_s if x[0] == r][0] * .55, 2), [x[3] for x in roles_s if x[0] == r][0], 1 if r == 'Superintendent' else None, 390 if r == 'Superintendent' else 0)
     for i, (r, m, lab) in enumerate(TEAM)])
tw = []
nweeks = int(26 * 4.33) + 16
for tm in range(1, 8):
    for w in range(1, nweeks + 1):
        r = TEAM[tm - 1][0]
        h = 0
        if r in ('Estimator', 'Pre-construction manager'): h = 40 if w <= 16 else (8 if w <= 24 else 0)
        elif r in ('Project manager', 'Superintendent'): h = 20 if w <= 16 else 40
        elif r == 'Project engineer': h = 40 if 16 < w <= nweeks - 4 else 0
        elif r == 'Project executive': h = 8
        elif r == 'Safety manager': h = 20 if 16 < w <= nweeks - 6 else 0
        if h: tw.append((tm, S(f"SELECT WeekId FROM ProjectWeek WHERE ProjectId=({J('EST-24031')[2:-1]}) AND WeekNo={w}"), h))
ins('TeamMemberWeek', ['TeamMemberId','WeekId','Hours'], tw)
ins('TeamMemberActivity', ['TeamMemberId','ActivityId'], [(m, a) for m in range(1, 8) for a in (1, 3, 5)])
cl = []
for a, b, *_ in vers:
    if a not in ('EST-24031', 'EST-24044') and not a.startswith('EST-23'): continue
    sc = 1.0 if a == 'EST-24031' else .5
    for i, (cat, item, qty, uom, rate) in enumerate(PRECON_KIT):
        cl.append((V(a, b), 'precon', cat, item, i + 1, round(qty * sc, 2), uom, rate, round(rate * 1.1, 2), True))
    for i, (cat, item, qty, uom, rate) in enumerate(GENREQ_KIT):
        cl.append((V(a, b), 'genreq', cat, item, i + 1, round(qty * sc, 2), uom, rate, round(rate * 1.08, 2), cat != 'Safety'))
ins('CostLine', ['VersionId','Area','CategoryName','ItemName','SortKey','Qty','UomCode','Rate','QuotedRate','IsBillable'], cl)

# ---------------- portal, proposal reader, activity ----------------
ins('BidInvite', ['TradeId','PartnerId','SentToEmail','TokenHash','Status','Message','SentAt','OpenedAt','ExpiresAt','SentBy'],
    [(CW, P('Skyframe Glass'), mail('Skyframe Glass', 0), S("digest('demo-invite-skyframe','sha256')"), 'opened', 'Please confirm your curtain wall pricing by Nov 12.',
      dt.datetime(2026, 10, 28, 14, 0), dt.datetime(2026, 10, 29, 9, 30), dt.datetime(2026, 11, 18, 23, 59), U('alice.chen'))])
ins('ActivityEvent', ['UserId','ProjectId','VersionId','Verb','SubjectType','SubjectKey','AfterState'],
    [(U('alice.chen'), J('EST-24031'), V('EST-24031', 2), 'answer.update', 'answer', 'curtain-wall', '{"kind":"included"}'),
     (U('mia.diaz'), J('EST-24031'), V('EST-24031', 2), 'trade.note', 'trade', '05 12 00', '{"note":"Allied lowest after scope match"}')])
out.append("""-- bring stored bid totals in line with the scope answers (same rule the API uses)
UPDATE Bid b SET ScopeAdjustments = x.adj, TotalAmount = CASE WHEN b.BaseAmount IS NULL THEN NULL ELSE b.BaseAmount + x.adj END
FROM (SELECT b2.BidId, coalesce(sum(a.Amount) FILTER (WHERE a.Kind='amount' AND l.Section IN ('baserequirement','tradeitem') AND NOT l.SkipInAdjustments AND l.ArchivedAt IS NULL),0) adj
      FROM Bid b2 LEFT JOIN ScopeAnswer a ON a.BidId=b2.BidId LEFT JOIN ScopeLine l ON l.LineId=a.LineId GROUP BY b2.BidId) x
WHERE b.BidId = x.BidId;
UPDATE Trade t SET LowestValue = (SELECT min(TotalAmount) FROM Bid WHERE TradeId=t.TradeId AND IsPosted AND State<>'disqualified'),
  CarriedValue = coalesce((SELECT TotalAmount + t.Adjustment FROM Bid WHERE TradeId=t.TradeId AND IsCarried), t.PlugAmount);
UPDATE Trade SET BudgetDelta = CarriedValue - BudgetValue;""")
out.append("COMMIT;")
open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'migrations', '002_seed.sql'), 'w').write("\n\n".join(out) + "\n")
print('seed written')
