// End-to-end smoke test against a running API (node ≥ 20, no packages).
//   node api/tests/smoke.mjs [http://localhost:5080]
// Signs up a throw-away company, checks it cannot see the demo company's data, exercises invitations,
// password reset, lockout and the admin endpoints. Exits 1 on the first failure.
const base = (process.argv[2] ?? 'http://localhost:5080').replace(/\/$/, '')
const stamp = Date.now().toString(36)
let failures = 0
const ok = (cond, what) => { if (cond) console.log('  ✓', what); else { failures++; console.log('  ✗', what) } }

async function call(method, path, { token, body, raw } = {}) {
  const headers = { 'X-Tab-Id': 'smoke' }
  if (token) headers.Authorization = `Bearer ${token}`
  if (body !== undefined) headers['Content-Type'] = 'application/json'
  const res = await fetch(base + '/api' + path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) })
  const text = await res.text()
  let data = null; try { data = text ? JSON.parse(text) : null } catch { data = text }
  if (raw) return { status: res.status, data }
  return { status: res.status, data }
}

console.log(`Smoke test → ${base}`)
const health = await call('GET', '/health')
ok(health.status === 200 && health.data.ok, `health ok · schema ${health.data?.schema}`)
ok(health.data?.schema >= 9, 'schema is 9 or later (companies)')

// ---------------------------------------------------------------- demo company sign-in
const demo = await call('POST', '/auth/login', { body: { email: 'k.raman@northshore.example', password: 'Demo@123' } })
ok(demo.status === 200, 'demo admin signs in')
const demoTok = demo.data?.accessToken
ok(demo.data?.me?.company?.name === 'Northshore Builders', `me carries the company (${demo.data?.me?.company?.name})`)
const demoProjects = await call('GET', '/projects', { token: demoTok })
ok(demoProjects.status === 200 && demoProjects.data.length > 0, `demo company sees its projects (${demoProjects.data?.length})`)
const firstNo = demoProjects.data?.[0]?.projectNo
const demoDetail = await call('GET', `/projects/${encodeURIComponent(firstNo)}`, { token: demoTok })
const demoVersionId = demoDetail.data?.versions?.[0]?.versionId
ok(demoDetail.status === 200 && demoVersionId, `demo project ${firstNo} opens (version ${demoVersionId})`)
const demoPartners = await call('GET', '/partners', { token: demoTok })
ok(demoPartners.status === 200 && demoPartners.data.length > 0, `demo company has trade partners (${demoPartners.data?.length})`)

// ---------------------------------------------------------------- a brand-new company
console.log('\nSign-up')
const weak = await call('POST', '/auth/signup', { body: { companyName: `Acme ${stamp}`, givenName: 'Ada', familyName: 'Lovelace', email: `ada.${stamp}@acme.example`, password: 'short' } })
ok(weak.status === 400, 'weak password is rejected')
const su = await call('POST', '/auth/signup', { body: { companyName: `Acme Builders ${stamp}`, givenName: 'Ada', familyName: 'Lovelace', email: `ada.${stamp}@acme.example`, password: 'Correct-Horse-9' } })
ok(su.status === 201, `sign-up creates the company (${su.status})`)
const tok = su.data?.accessToken
ok(su.data?.me?.roles?.includes('admin'), 'first user is an administrator')
const dup = await call('POST', '/auth/signup', { body: { companyName: 'Dup', givenName: 'A', familyName: 'B', email: `ada.${stamp}@acme.example`, password: 'Correct-Horse-9' } })
ok(dup.status === 409, 'same email cannot sign up twice')

console.log('\nIsolation')
const mine = await call('GET', '/projects', { token: tok })
ok(mine.status === 200 && mine.data.length === 0, 'new company has no projects')
const peek = await call('GET', `/projects/${encodeURIComponent(firstNo)}`, { token: tok })
ok(peek.status === 404, `demo project ${firstNo} is not found for the new company (${peek.status})`)
const board = await call('GET', `/versions/${demoVersionId}/bid-board`, { token: tok })
ok(board.status === 404, `demo version bid board is not found (${board.status})`)
const roll = await call('GET', `/versions/${demoVersionId}/price-roll-up`, { token: tok })
ok(roll.status === 404, `demo version roll-up is not found (${roll.status})`)
const partners = await call('GET', '/partners', { token: tok })
ok(partners.status === 200 && partners.data.length === 0, 'trade partner directory is empty')
const bench = await call('GET', '/benchmarks', { token: tok })
ok(bench.status === 200 && bench.data.length === 0, 'no benchmarks leak from other companies')
const lk = await call('GET', '/lookups', { token: tok })
ok(lk.data?.staffRoles?.length === 8 && lk.data?.scopeKits?.length === 2 && lk.data?.costKits?.length === 2, `starter library copied (roles ${lk.data?.staffRoles?.length}, scope kits ${lk.data?.scopeKits?.length}, cost kits ${lk.data?.costKits?.length})`)
ok(lk.data?.users?.length === 1, 'lookups show only this company\'s people')

console.log('\nFirst project')
const np = await call('POST', '/projects', { token: tok, body: { projectName: 'Smoke Tower', clientName: 'Smoke Client', city: 'Austin', stateCode: 'TX', projectType: 'Office' } })
ok(np.status === 201 && /^EST-\d+$/.test(np.data?.projectNo ?? ''), `project created ${np.data?.projectNo}`)
const det = await call('GET', `/projects/${encodeURIComponent(np.data?.projectNo)}`, { token: tok })
const vid = det.data?.versions?.[0]?.versionId
const tr = await call('POST', `/versions/${vid}/trades`, { token: tok, body: { csiCode: '03 30 00', tradeName: 'Concrete', budgetValue: 250000 } })
ok(tr.status === 200 || tr.status === 201, `trade added (${tr.status})`)
const bb = await call('GET', `/versions/${vid}/bid-board`, { token: tok })
const tradeId = bb.data?.trades?.[0]?.tradeId
ok(bb.status === 200 && tradeId, 'bid board loads with the trade')
const bid = await call('POST', `/trades/${tradeId}/bids`, { token: tok, body: { bidderName: `Smoke Concrete Co ${stamp}`, email: 'bids@smokeconcrete.example', baseAmount: 240000 } })
ok(bid.status === 200 || bid.status === 201, `bidder added (${bid.status})`)
const partners2 = await call('GET', '/partners', { token: tok })
ok(partners2.data?.length === 1, 'directory now has exactly one partner')
const demoPartners2 = await call('GET', '/partners', { token: demoTok })
ok(demoPartners2.data?.length === demoPartners.data?.length, 'demo company directory unchanged')
const demoPeek = await call('GET', `/versions/${vid}/bid-board`, { token: demoTok })
ok(demoPeek.status === 404, 'demo company cannot open the new company\'s version')
const req = await call('GET', `/versions/${vid}/requirements`, { token: tok })
ok(req.status === 200 && req.data?.templates?.length === 1, 'base requirement templates come from the copied library')

console.log('\nAdministration')
const inv = await call('POST', '/admin/users/invite', { token: tok, body: { email: `bob.${stamp}@acme.example`, givenName: 'Bob', familyName: 'Builder', role: 'estimator' } })
ok(inv.status === 200 && inv.data?.link, 'invitation created')
const invToken = inv.data?.link?.split('/invite/')[1]
const invPeek = await call('GET', `/auth/invite/${invToken}`)
ok(invPeek.status === 200 && invPeek.data?.companyName?.startsWith('Acme Builders'), 'invitation link resolves to the company')
const acc = await call('POST', '/auth/invite/accept', { body: { token: invToken, password: 'Bobs-Password-77' } })
ok(acc.status === 200 && acc.data?.me?.roles?.includes('estimator'), 'invitation accepted as estimator')
const bobTok = acc.data?.accessToken
const bobProjects = await call('GET', '/projects', { token: bobTok })
ok(bobProjects.status === 200 && bobProjects.data.length === 1, 'new estimator sees the company project')
const users = await call('GET', '/admin/users', { token: tok })
ok(users.data?.users?.length === 2, 'admin sees two users')
const noAdmin = await call('PATCH', '/admin/company', { token: bobTok, body: { displayName: 'Hacked' } })
ok(noAdmin.status === 403, 'estimator cannot change company settings')
const co = await call('PATCH', '/admin/company', { token: tok, body: { displayName: `Acme Builders ${stamp} Inc`, projectNoPrefix: 'ACM-' } })
ok(co.status === 200 && co.data?.projectNoPrefix === 'ACM-', 'admin updates company settings')
const np2 = await call('POST', '/projects', { token: tok, body: { projectName: 'Second' } })
ok(np2.data?.projectNo?.startsWith('ACM-'), `new prefix is used (${np2.data?.projectNo})`)
const staff = await call('POST', '/admin/staff', { token: tok, body: { fullName: 'Grace Hopper', staffRoleId: lk.data.staffRoles[0].staffRoleId, hourlyCost: 95, teamGroup: 'corporate' } })
ok(staff.status === 201, 'employee added')
const audit = await call('GET', '/admin/audit', { token: tok })
ok(audit.status === 200 && audit.data.some(e => e.verb === 'user.invite'), 'audit trail lists the invitation')
const tpl = await call('GET', '/admin/templates', { token: tok })
ok(tpl.status === 200 && tpl.data?.scopeKits?.length === 2 && tpl.data?.preconTasks?.length > 0, 'template library lists kits and precon tasks')

console.log('\nPasswords and lockout')
const forgot = await call('POST', '/auth/forgot', { body: { email: `bob.${stamp}@acme.example` } })
ok(forgot.status === 200 && forgot.data?.sent, 'forgot password answers the same for any address')
if (forgot.data?.devLink) {
  const rt = forgot.data.devLink.split('/reset/')[1]
  const reset = await call('POST', '/auth/reset', { body: { token: rt, password: 'New-Password-88' } })
  ok(reset.status === 200, 'reset link sets a new password')
  const old = await call('POST', '/auth/login', { body: { email: `bob.${stamp}@acme.example`, password: 'Bobs-Password-77' } })
  ok(old.status === 401, 'old password no longer works')
  const nw = await call('POST', '/auth/login', { body: { email: `bob.${stamp}@acme.example`, password: 'New-Password-88' } })
  ok(nw.status === 200, 'new password works')
  const stale = await call('GET', '/me', { token: bobTok })
  ok(stale.status === 401, 'reset signed out the other session')
} else console.log('  · reset link not shown (SMTP configured or not Development); skipped reset checks')
let last = 0
for (let i = 0; i < 9; i++) { const r = await call('POST', '/auth/login', { body: { email: `ada.${stamp}@acme.example`, password: 'wrong-' + i } }); last = r.status }
ok(last === 423 || last === 429, `account locks or rate-limits after repeated failures (${last})`)
const chg = await call('POST', '/auth/change-password', { token: tok, body: { currentPassword: 'Correct-Horse-9', newPassword: 'Even-Better-10' } })
ok(chg.status === 200, 'change password works while signed in')

console.log(failures ? `\n${failures} check(s) failed` : '\nAll checks passed')
process.exit(failures ? 1 : 0)
