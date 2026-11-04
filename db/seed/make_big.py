#!/usr/bin/env python3
"""Fill the Gridline import template with a large test project: 230+ trades, 10-14 trade partners each.
All company names, contacts, emails (.example) and phone numbers (555-01xx) are made up for testing."""
import random, sys, datetime as dt
import openpyxl

random.seed(26100)
SRC, OUT = sys.argv[1], sys.argv[2]

# (CSI section, trade name, typical budget in $k for a ~$250M mixed-use tower)
TRADES = """02 32 00|Geotechnical investigations|180
02 41 00|Demolition|950
02 41 19|Selective demolition|420
02 82 00|Asbestos remediation|310
02 83 00|Lead remediation|140
03 11 00|Concrete forming|6800
03 15 00|Concrete accessories|380
03 21 00|Reinforcing steel|5200
03 23 00|Post-tensioning|2400
03 30 00|Cast-in-place concrete|11800
03 35 00|Concrete finishing|1300
03 41 00|Precast structural concrete|3900
03 45 00|Architectural precast|4600
03 47 00|Tilt-up concrete|1700
03 54 00|Cast underlayment|520
03 60 00|Grouting|260
03 81 00|Concrete cutting and coring|210
04 01 20|Masonry restoration|330
04 20 00|Unit masonry|2600
04 21 13|Brick masonry|3100
04 22 00|Concrete unit masonry|1900
04 40 00|Stone assemblies|1450
04 72 00|Cast stone|680
05 12 00|Structural steel|14200
05 21 00|Steel joists|1600
05 31 00|Steel deck|2100
05 40 00|Cold-formed metal framing|3300
05 50 00|Miscellaneous metals|2200
05 51 00|Metal stairs|1350
05 52 00|Metal railings|980
05 53 00|Metal gratings|240
05 58 00|Formed metal fabrications|300
05 70 00|Decorative metal|760
05 73 00|Decorative metal railings|890
05 75 00|Decorative formed metal|410
06 10 00|Rough carpentry|1450
06 16 00|Sheathing|720
06 17 53|Wood trusses|380
06 20 00|Finish carpentry|1150
06 40 00|Architectural woodwork|2900
06 41 00|Architectural casework|2300
06 42 00|Wood paneling|640
06 60 00|Plastic fabrications|190
06 61 16|Solid surfacing|560
07 11 00|Dampproofing|210
07 13 00|Sheet waterproofing|1250
07 14 00|Fluid-applied waterproofing|980
07 16 00|Cementitious waterproofing|340
07 19 00|Water repellents|150
07 21 00|Thermal insulation|1600
07 24 00|EIFS|1350
07 25 00|Weather barriers|620
07 27 00|Air barriers|1100
07 31 00|Shingles|180
07 41 13|Metal roof panels|1450
07 42 13|Metal wall panels|3800
07 42 43|Composite wall panels|2700
07 46 00|Siding|560
07 50 00|Membrane roofing|2300
07 52 00|Modified bituminous roofing|1250
07 54 00|TPO roofing|1500
07 55 00|Protected membrane roofing|880
07 56 00|Fluid-applied roofing|430
07 62 00|Sheet metal flashing and trim|870
07 71 00|Roof specialties|360
07 72 00|Roof accessories|290
07 81 00|Spray fireproofing|1850
07 84 00|Firestopping|1050
07 92 00|Joint sealants|820
07 95 00|Expansion control|460
08 11 13|Hollow metal doors and frames|1650
08 14 00|Wood doors|1900
08 31 00|Access doors and panels|180
08 33 00|Coiling doors and grilles|720
08 34 00|Special function doors|410
08 36 00|Sectional doors|380
08 38 00|Traffic doors|160
08 41 13|Aluminum entrances and storefronts|2400
08 42 29|Automatic entrances|520
08 43 00|Storefronts|1800
08 44 13|Glazed aluminum curtain wall|18600
08 45 00|Translucent wall assemblies|640
08 51 13|Aluminum windows|4300
08 56 00|Special function windows|380
08 62 00|Unit skylights|240
08 63 00|Metal-framed skylights|690
08 71 00|Door hardware|2100
08 80 00|Glazing|2700
08 83 00|Mirrors|260
08 87 00|Glazing films|190
08 90 00|Louvers and vents|570
09 21 16|Gypsum board assemblies|9800
09 22 16|Non-structural metal framing|4700
09 24 00|Cement plaster|880
09 27 00|Plaster fabrications|320
09 29 00|Gypsum board finishing|2600
09 30 00|Tiling|4100
09 51 00|Acoustical ceilings|2300
09 54 00|Specialty ceilings|1150
09 62 00|Specialty flooring|720
09 64 00|Wood flooring|1650
09 65 00|Resilient flooring|1900
09 66 00|Terrazzo|1350
09 67 00|Fluid-applied flooring|610
09 68 00|Carpet|1450
09 69 00|Access flooring|560
09 72 00|Wall coverings|990
09 77 00|Special wall surfacing|430
09 81 00|Acoustic insulation|560
09 84 00|Acoustic room components|380
09 91 00|Painting|3400
09 93 00|Staining and transparent finishes|240
09 96 00|High-performance coatings|690
09 97 00|Special coatings|330
10 11 00|Visual display units|210
10 14 00|Signage|780
10 21 13|Toilet compartments|490
10 22 00|Partitions|360
10 22 39|Operable partitions|540
10 26 00|Wall and door protection|330
10 28 00|Toilet accessories|620
10 44 00|Fire protection specialties|190
10 51 00|Lockers|270
10 56 00|Storage assemblies|420
10 73 00|Protective covers and canopies|560
10 75 00|Flagpoles|60
10 81 00|Pest control devices|70
11 13 00|Loading dock equipment|480
11 30 00|Residential equipment|540
11 31 00|Residential appliances|2900
11 40 00|Foodservice equipment|1850
11 52 00|Audio-visual equipment|640
11 53 00|Laboratory equipment|420
11 61 00|Theater and stage equipment|360
11 66 00|Athletic equipment|310
11 70 00|Healthcare equipment|280
11 82 00|Trash compactors and chutes equipment|240
12 21 00|Window blinds|420
12 24 00|Window shades|860
12 36 00|Countertops|1750
12 48 00|Entrance mats|150
12 50 00|Furniture|1300
12 61 00|Fixed audience seating|380
12 93 00|Site furnishings|330
13 11 00|Swimming pools|1250
13 21 00|Controlled environment rooms|290
13 34 00|Fabricated engineered structures|540
13 48 00|Seismic and vibration control|370
13 49 00|Radiation protection|180
14 21 00|Traction elevators|8900
14 24 00|Hydraulic elevators|760
14 31 00|Escalators|1900
14 42 00|Wheelchair lifts|160
14 91 00|Trash and linen chutes|310
21 11 00|Fire water service piping|420
21 13 00|Fire sprinklers|4800
21 22 00|Clean-agent fire suppression|380
21 30 00|Fire pumps|520
22 05 00|Plumbing general requirements|300
22 07 00|Plumbing insulation|690
22 11 00|Domestic water distribution|5100
22 13 00|Sanitary sewerage|3200
22 14 00|Storm drainage|1700
22 30 00|Plumbing equipment|1350
22 34 00|Domestic water heaters|860
22 40 00|Plumbing fixtures|4200
22 47 00|Drinking fountains and water coolers|160
22 60 00|Medical and lab gas systems|540
22 66 00|Chemical waste systems|210
23 05 93|Testing, adjusting and balancing|620
23 07 00|HVAC insulation|1550
23 09 00|HVAC controls|3300
23 21 13|Hydronic piping|5600
23 23 00|Refrigerant piping|1900
23 31 13|Sheet metal ductwork|7400
23 34 00|HVAC fans|980
23 36 00|Air terminal units|1250
23 37 00|Grilles, registers and diffusers|870
23 41 00|Air filtration|330
23 52 00|Boilers|1450
23 57 00|Heat exchangers|560
23 64 00|Chillers|3100
23 65 00|Cooling towers|1350
23 73 00|Air-handling units|4600
23 74 00|Rooftop units|1850
23 81 26|Split-system air conditioners|2200
23 82 00|Fan coils and unit heaters|3700
23 84 00|Humidity control|290
25 50 00|Integrated building automation|1250
26 05 00|Electrical general requirements|480
26 05 19|Building wire and cable|4900
26 05 33|Raceways and boxes|3800
26 09 23|Lighting controls|1300
26 11 00|Substations|1700
26 12 00|Medium-voltage transformers|1250
26 22 00|Dry-type transformers|690
26 24 13|Switchboards|1850
26 24 16|Panelboards|1450
26 27 26|Wiring devices|980
26 28 00|Overcurrent protective devices|420
26 32 13|Emergency generators|2600
26 33 53|UPS systems|760
26 36 00|Automatic transfer switches|540
26 41 00|Lightning protection|260
26 43 00|Surge protection|130
26 51 00|Interior lighting|5200
26 56 00|Exterior lighting|970
27 05 00|Communications general requirements|170
27 10 00|Structured cabling|2600
27 41 00|Audio-video systems|870
27 51 00|Distributed audio systems|420
27 52 00|Nurse call and monitoring|260
27 53 00|Distributed antenna system (DAS)|1150
28 10 00|Access control|1050
28 20 00|Video surveillance|890
28 31 00|Intrusion detection|240
28 46 00|Fire alarm|2300
28 47 00|Mass notification|360
31 10 00|Site clearing|380
31 22 00|Grading|720
31 23 00|Excavation and fill|5400
31 25 00|Erosion and sediment control|260
31 31 00|Termite soil treatment|70
31 41 00|Shoring|2100
31 50 00|Excavation support and dewatering|2900
31 62 00|Driven piles|3300
31 63 00|Drilled shafts (caissons)|4100
31 66 00|Special foundations|1450
32 12 16|Asphalt paving|980
32 13 13|Concrete paving|1250
32 14 00|Unit pavers|860
32 16 00|Curbs, gutters and sidewalks|740
32 17 23|Pavement markings|90
32 18 00|Athletic and recreational surfacing|330
32 31 00|Fences and gates|410
32 32 00|Retaining walls|1250
32 84 00|Irrigation|360
32 91 00|Planting soils|420
32 92 00|Turf and grasses|190
32 93 00|Trees and plants|880
33 11 00|Site water distribution|1150
33 31 00|Site sanitary sewer|940
33 41 00|Site storm drainage|1350
33 46 00|Subdrainage|380
33 71 00|Site electrical utilities|1450
33 81 00|Site communications structures|260"""
rows = [l.split('|') for l in TRADES.strip().splitlines()]
assert len({r[0] for r in rows}) == len(rows), 'CSI codes must be unique'

SPEC = {'02': ['Demolition', 'Environmental', 'Abatement', 'Site Services'], '03': ['Concrete', 'Structures', 'Formwork', 'Constructors'], '04': ['Masonry', 'Stone Works', 'Brick & Block'],
        '05': ['Steel', 'Iron Works', 'Metal Fabricators', 'Erectors'], '06': ['Carpentry', 'Millwork', 'Woodworks', 'Interiors'], '07': ['Roofing', 'Waterproofing', 'Envelope', 'Building Systems', 'Insulation'],
        '08': ['Glass & Glazing', 'Door & Hardware', 'Curtainwall', 'Openings', 'Window Systems'], '09': ['Interiors', 'Drywall', 'Flooring', 'Tile & Stone', 'Finishes', 'Painting'],
        '10': ['Specialties', 'Building Products', 'Supply'], '11': ['Equipment', 'Systems', 'Supply'], '12': ['Furnishings', 'Interiors', 'Supply'], '13': ['Special Structures', 'Pools', 'Systems'],
        '14': ['Elevator', 'Vertical Transport', 'Lift Systems'], '21': ['Fire Protection', 'Sprinkler', 'Fire Systems'], '22': ['Plumbing', 'Mechanical', 'Piping'],
        '23': ['Mechanical', 'HVAC', 'Air Systems', 'Sheet Metal', 'Controls'], '25': ['Controls', 'Automation'], '26': ['Electric', 'Electrical', 'Power Systems', 'Lighting'],
        '27': ['Communications', 'Low Voltage', 'Technologies', 'Data Systems'], '28': ['Security', 'Fire & Life Safety', 'Systems', 'Low Voltage'],
        '31': ['Excavating', 'Earthworks', 'Foundations', 'Site Contractors'], '32': ['Paving', 'Landscape', 'Sitework', 'Hardscape'], '33': ['Utilities', 'Underground', 'Pipeline', 'Site Utilities']}
P1 = ['Granite', 'Copper', 'Harbor', 'Cedar', 'Summit', 'Iron', 'Blue', 'Red', 'Silver', 'North', 'South', 'East', 'West', 'Lake', 'River', 'Pine', 'Oak', 'Maple', 'Stone', 'Bright',
      'Tide', 'Prairie', 'Canyon', 'Falcon', 'Heron', 'Bay', 'Ridge', 'Clear', 'High', 'Gold', 'Amber', 'Slate', 'Willow', 'Aspen', 'Juniper', 'Beacon', 'Anchor', 'Compass', 'Keystone', 'Lantern']
P2 = ['field', 'line', 'point', 'gate', 'stone', 'wood', 'brook', 'crest', 'view', 'haven', 'mark', 'side', 'water', 'ford', 'ridge', 'hollow', 'run', 'land', 'bridge', 'well']
ENT = ['Inc.', 'LLC', 'Co.', 'Group', 'Corp.', 'Contractors', 'Partners', '& Sons']
FIRST = 'J. M. R. D. T. A. K. S. L. P. C. B. E. N. G. H.'.split()
LAST = ['Ortega', 'Park', 'Moore', 'Nguyen', 'Patel', 'Garcia', 'Kim', 'Johnson', 'Rivera', 'Chen', 'Brooks', 'Diaz', 'Foster', 'Hughes', 'Reyes', 'Shah', 'Turner', 'Walsh', 'Young', 'Price']

used = set()
def company(div):
    while True:
        n = f"{random.choice(P1)}{random.choice(P2)} {random.choice(SPEC.get(div, ['Contractors']))} {random.choice(ENT)}"
        if n.lower() not in used: used.add(n.lower()); return n
# a pool per division so the same partners bid several trades, like real life
pool = {d: [company(d) for _ in range(28)] for d in SPEC}

wb = openpyxl.load_workbook(SRC)
def header_row(ws, first):
    for r in range(1, 12):
        if str(ws.cell(r, 1).value or '').startswith(first): return r
    raise SystemExit(f'header {first} not found on {ws.title}')
def clear_below(ws, hr):
    if ws.max_row > hr: ws.delete_rows(hr + 1, ws.max_row - hr)

# --- Project
ws = wb['Project']; hr = header_row(ws, 'Field')
vals = {'Project name': 'Harborview Mixed-Use Tower', 'Project number': 'EST-26100', 'Client': 'Harborview Development Partners LLC', 'Business unit': 'Commercial',
        'Project type': 'Office', 'Contract type': 'GMP', 'Stage': 'Bidding', 'Address': '1200 Harbor Point Blvd', 'City': 'Norfolk', 'State': 'VA', 'County': 'Norfolk City',
        'Bid due date': dt.datetime(2027, 3, 19), 'Construction start': dt.datetime(2027, 6, 7), 'Duration (months)': 30, 'Gross SF': 1185000, 'Fee %': 3.75,
        'Reserve (contingency) %': 2.5, 'Works cover (builders risk) rate': 0.16, 'Estimator': None, 'Project manager': None}
for r in range(hr + 1, ws.max_row + 1):
    k = ws.cell(r, 1).value
    if k in vals: ws.cell(r, 2).value = vals[k]

# --- Zones
ws = wb['Zones']; hr = header_row(ws, 'Zone code'); clear_below(ws, hr)
zones = [('TWR', 'Residential tower', 720000, 540, 58), ('OFF', 'Office floors', 260000, None, 22), ('RET', 'Podium retail', 85000, None, 7), ('PKG', 'Parking garage', 95000, 620, 9), ('SITE', 'Sitework', 25000, None, 4)]
for z in zones: ws.append(list(z))

# --- Trades and bidders
wt = wb['Trades']; ht = header_row(wt, 'CSI code'); clear_below(wt, ht)
wbd = wb['Bidders']; hb = header_row(wbd, 'CSI code'); clear_below(wbd, hb)
est = ['A. Chen', 'M. Diaz', 'K. Raman']; nb = 0
STATES = ['Bid received'] * 7 + ['Bidding'] * 2 + ['Invited', 'Declined', 'Late', 'Undecided']
for i, (code, name, k) in enumerate(rows):
    budget = round(int(k) * 1000 * random.uniform(0.9, 1.1), -3)
    due = dt.datetime(2027, 3, 5) + dt.timedelta(days=7 * (i % 3))
    wt.append([code, name, budget, None, None, est[i % 3], due, None])
    div = code[:2]; n = random.randint(10, 14)
    names = random.sample(pool[div], n)
    states = ['Bid received'] * 3 + [random.choice(STATES) for _ in range(n - 3)]; random.shuffle(states)
    for nm, st in zip(names, states):
        first, last = random.choice(FIRST), random.choice(LAST); dom = ''.join(c for c in nm.split()[0].lower() if c.isalpha())
        base = round(budget * random.uniform(0.86, 1.2), -2) if st in ('Bid received', 'Late') else None
        wbd.append([code, nm, f'{first} {last}', f'estimating@{dom}.example', f'(757) 555-01{random.randint(0, 99):02d}', st, base, None,
                    'Yes' if random.random() < 0.18 else 'No', None]); nb += 1

# --- Crew (a small team so the crew plan isn't empty)
ws = wb['Crew']; hr = header_row(ws, 'Role'); clear_below(ws, hr)
for r in [('Project executive', 'TBA', 1, 30, 0.5, 112, 'No', 'Construction'), ('Senior project manager', 'TBA', 1, 30, 1, 98, 'No', 'Construction'),
          ('Project manager', 'TBA', 2, 30, 1, 84, 'No', 'Construction'), ('General superintendent', 'TBA', 1, 30, 1, 96, 'Yes', 'Construction'),
          ('Superintendent', 'TBA', 3, 29, 2, 82, 'Yes', 'Construction'), ('Project engineer', 'TBA', 2, 30, 2, 58, 'No', 'Construction'),
          ('Safety manager', 'TBA', 1, 30, 1, 66, 'No', 'Construction'), ('Estimator', 'TBA', 1, 3, 1, 80, 'No', 'Preconstruction')]:
    ws.append(list(r))

# --- Base requirements
ws = wb['Base Requirements']; hr = header_row(ws, 'Section'); clear_below(ws, hr)
for r in [('General terms', None, 'Furnish all labor, materials, equipment and supervision for a complete system per the drawings and specifications.', 'Included', 'Yes'),
          ('General terms', None, 'Bid is valid for 90 days from the bid due date.', 'Included', 'Yes'),
          ('Insurance & bonding', None, 'Payment and performance bond at 100% of subcontract value.', 'Price', 'Yes'),
          ('Insurance & bonding', None, 'Builders risk deductible responsibility up to $25,000.', 'Excluded', 'No'),
          ('Site logistics', None, 'Hoisting by trade partner where not covered by GC tower crane schedule.', 'Included', 'No'),
          ('Site logistics', None, 'Offsite parking and shuttling for all personnel.', 'Included', 'Yes'),
          ('Schedule', None, 'Work sequenced floor by floor per the GC master schedule; second shift at no added cost.', 'Included', 'Yes'),
          ('Cleanup', None, 'Daily cleanup and debris removal to GC dumpster.', 'Included', 'Yes')]:
    ws.append(list(r))

# leave the later sheets empty so the import skips them
for s in ['Qualifications', 'Provisional Sums', 'Options & Savings', 'Unit Rates']:
    if s in wb.sheetnames:
        ws = wb[s]; clear_below(ws, header_row(ws, ws.cell(header_row(ws, 'Type') if s in ('Qualifications', 'Provisional Sums') else header_row(ws, 'Kind') if s == 'Options & Savings' else header_row(ws, 'CSI code'), 1).value))
wb.save(OUT)
print(f'{len(rows)} trades, {nb} bidder rows, {len(used)} trade partners -> {OUT}')
