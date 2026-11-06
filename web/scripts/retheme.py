# One-off: remap the old orchid/tangerine palette in src/styles/app.css to the forest & lime theme.
# Kept in the repo so the mapping is on record; safe to re-run (idempotent).
import re, sys, pathlib
p = pathlib.Path(__file__).resolve().parent.parent / 'src' / 'styles' / 'app.css'
s = p.read_text(encoding='utf-8')
m = {
 '#6a32d4':'#27624d','#5524b4':'#1e4c3b','#3341c9':'#27624d','#2733a6':'#1e4c3b','#7a5cf0':'#4e9a78','#9b5cf6':'#3f8a6c','#b26bff':'#6fb08f','#8b4ef0':'#35775d','#4a1fa3':'#1e4c3b',
 '#5b2ec4':'#27624d','#5119b7':'#27624d','#4b2bb0':'#3d5a2a','#7b6aa8':'#5d744b','#2a1a66':'#1e4c3b','#4b2a9a':'#27624d',
 '#f2ecfd':'#eaf2ed','#f1ebfc':'#eaf2ed','#f3effd':'#eaf2ed','#eceefd':'#eaf2ed','#efe3ff':'#e5f2ec','#efe7ff':'#e9f1ea','#e8e2ff':'#e6eedf','#e3d5fb':'#cfe3d8','#dde1fb':'#cfe3d8','#dcc6ff':'#c5d6c9','#cbb6f5':'#c5d6c9','#cfc6ea':'#c5d6c9',
 '#f5f2fd':'#eef1ea','#fbf9ff':'#f7f8f4','#faf8ff':'#f7f8f4','#fcfbff':'#f7f8f4','#f6f2ff':'#eef2e7','#f4eeff':'#eef2e7','#e5dcfa':'#dce2db','#f7f5fc':'#f3f5ef','#f4f2fa':'#eef1ea','#f5f0ff':'#eef2e7',
 '#ddd6ee':'#e4e8e1','#eee9f8':'#e4e8e1','#f2eff9':'#e4e8e1','#a7a1bd':'#9aa89e','#b3abcf':'#9aa89e','#c9c3dc':'#9aa89e','#262b5c':'#223a30','#2a2f63':'#223a30','#aab0e0':'#9daaa1','#c9cdf0':'#c1cbc4','#e8dcff':'#c1cbc4','#efe6ff':'#dfe7dd','#f1e9ff':'#dfe7dd',
 '#211d33':'#18221f','#161a2e':'#18221f','#2d3252':'#34453a','#3d3755':'#34453a','#716b88':'#646f68','#6a6f8e':'#646f68','#a39dba':'#8f9a92','#9a9eb8':'#8f9a92','#ece9f4':'#e4e8e1','#e7e8f0':'#e4e8e1','#ddd8ea':'#dce2db','#d8dae6':'#dce2db',
 '#f3f1f9':'#eef1ea','#f0f1f7':'#eef1ea','#f8f5ff':'#f6f8f2','#f3f4fb':'#f6f8f2','#f8f7fc':'#f5f5f0','#f5f5f8':'#f5f5f0','#fafafd':'#f7f8f4','#f6f7fc':'#eef1ea','#12152a':'#18221f','#1b1f3a':'#243029','#a9adca':'#c1cbc4',
 '#1d1b2e':'#18221f','#2a2640':'#18221f','#3b3754':'#34453a','#4a4560':'#34453a','#4f4968':'#34453a','#6f749a':'#828a80','#7a7fa6':'#828a80','#d5d8ef':'#e2e9df','#5a5f84':'#8f9a92','#c4c7d8':'#b7c1b9',
 '#ff7a2f':'#d8ef58','#b44a0c':'#5c6b14','#fff1e6':'#f4f9d9','#ffc49e':'#c9dd6a','#ffb17d':'#bcd35a','#fff3ea':'#f6fadf','#fff2e8':'#f3f8d4','#ffd9bf':'#e2ecb0','#c2410c':'#5c6b14','#fff8f2':'#fbfdf0','#fffaf5':'#fcfdf3','#fffcf9':'#fcfdf5','#f3e6da':'#e8edd6',
 '#ffb27a':'#e6f18f','#ffe1cc':'#eef6bf','#ffd2b3':'#e9f3a8','#f2a516':'#d8ef58','#fff4dc':'#f4f9d9','#9a6400':'#5c6b14','#ffcf70':'#e6f18f','#3a2600':'#283518','#ffc95c':'#d8ef58','#fff8e6':'#f6fadf',
 '#0c8f7a':'#27624d','#11806b':'#27624d','#e1f5f1':'#e5f2ec','#e2f4ef':'#e5f2ec','#d63c5e':'#b33632','#cc3d55':'#b33632','#d1344b':'#b33632','#fdebf0':'#fbeceb','#fdecef':'#fbeceb','#b7791f':'#946211','#a96d00':'#946211','#fff4dd':'#fff6e0','#fff3d6':'#fff6e0',
 '#2fbf71':'#3f8a6c','#16a34a':'#27624d','#0f7a4a':'#27624d','#dcf7e8':'#e5f2ec','#e3f7ec':'#e5f2ec','#b3324a':'#8f2b28',
}
for a, b in m.items():
    s = s.replace(a, b).replace(a.upper(), b)
s = s.replace('rgba(106,50,212,', 'rgba(39,98,77,').replace('rgba(51,65,201,', 'rgba(39,98,77,')
for suf in ('1a', '22', '33', '1f', '55', '66', '26'):
    s = s.replace('#6a32d4' + suf, '#27624d' + suf)

green = pathlib.Path(__file__).resolve().parent / 'theme-v3.css'
block = green.read_text(encoding='utf-8')
start = s.find('/* ---- theme ---- */')
if start < 0: start = s.find('/* ================= Theme v3')
end = s.index('/* ================= presence (who is here) ================= */')
s = s[:start] + block + '\n' + s[end:]

fixes = [
 ('.cap .flag.car{background:var(--accent);color:#fff}', '.cap .flag.car{background:var(--accent);color:var(--accent-ink)}'),
 ('font-weight:800;color:#fff;background:var(--accent);padding:1px 7px 1px 5px', 'font-weight:800;color:var(--accent-ink);background:var(--accent);padding:1px 7px 1px 5px'),
 ('.carry3.on{background:var(--accent);border-color:var(--accent);color:#fff}.carry3.on i{border-color:#fff;background:#fff;box-shadow:inset 0 0 0 2px var(--accent)}', '.carry3.on{background:var(--accent);border-color:#c7db50;color:var(--accent-ink)}.carry3.on i{border-color:var(--accent-ink);background:var(--accent-ink);box-shadow:inset 0 0 0 2px var(--accent)}'),
 ('.ladder .dot.car{background:var(--accent);border-color:var(--accent);color:#fff;', '.ladder .dot.car{background:var(--accent);border-color:#c7db50;color:var(--accent-ink);'),
]
for a, b in fixes:
    if a in s: s = s.replace(a, b)
    else: print('note: fix target already applied or missing:', a[:50])
p.write_text(s, encoding='utf-8')
print('remaining old tokens:', sorted(set(re.findall(r'#(?:6a32d4|ff7a2f|3341c9|211d33|161a2e)', s))))
print('lines:', s.count('\n'))
