// Start a project: blank, or import the Excel template (check → preview → create).
import { useState } from 'react'
import { api } from '../api/client'
import { Icon } from '../components/Icon'
import { money } from '../app/format'

interface Check { token: string; ok: boolean; fileName: string; counts: Record<string, number>; issues: { sheet: string; row: number | null; column: string | null; severity: 'error' | 'warning'; message: string }[]; project: { name: string; number: string | null; client: string | null }; trades: { code: string; title: string; budget: number | null; bidders: number; match: string }[]; partners: { matched: number; created: number } }
export default function StartProject({ onClose, onCreated }: { onClose(): void; onCreated(projectNo: string, versionNo: number): void }) {
  const [mode, setMode] = useState<'blank' | 'import'>('import')
  const [check, setCheck] = useState<Check | null>(null); const [busy, setBusy] = useState(false); const [err, setErr] = useState<string | null>(null)
  const [form, setForm] = useState({ projectName: '', clientName: '', city: '', stateCode: 'VA', projectType: '', bidDueOn: '' })
  async function upload(f?: File | null) {
    if (!f) return; setBusy(true); setErr(null)
    try { const fd = new FormData(); fd.append('file', f); setCheck(await api.post<Check>('/imports/check', fd)) } catch (e) { setErr((e as Error).message) } finally { setBusy(false) }
  }
  async function create() {
    setBusy(true); setErr(null)
    try {
      const r = mode === 'import' ? await api.post<{ projectNo: string; versionNo: number }>(`/imports/${check!.token}/create`)
                                  : await api.post<{ projectNo: string; versionNo: number }>('/projects', form)
      onCreated(r.projectNo, r.versionNo)
    } catch (e) { setErr((e as Error).message) } finally { setBusy(false) }
  }
  return (
    <div className="scrim" onClick={onClose}><div className="modal wide" role="dialog" aria-modal="true" aria-label="Start a project" onClick={e => e.stopPropagation()}>
      <div className="mh"><h3>Start a project</h3><button className="btn g ic x" onClick={onClose} aria-label="Close"><Icon name="x" /></button></div>
      <div className="mb">
        <div className="seg" style={{ marginBottom: 14 }}><button aria-pressed={mode === 'import'} onClick={() => setMode('import')}>Import from Excel</button><button aria-pressed={mode === 'blank'} onClick={() => setMode('blank')}>Start blank</button></div>
        {mode === 'import' ? (!check ? <div className="drop" onDragOver={e => e.preventDefault()} onDrop={e => { e.preventDefault(); upload(e.dataTransfer.files[0]) }}>
            <Icon name="upload" size="l" /><div style={{ margin: '8px 0 4px' }}><b>Drop the filled template here</b> or browse</div>
            <label className="btn p" style={{ marginTop: 10 }}><Icon name="folder" />Choose file<input type="file" accept=".xlsx" hidden onChange={e => upload(e.target.files?.[0])} /></label>
            <a className="btn" style={{ marginLeft: 8 }} href="/api/template.xlsx">Download template</a>{busy && <p className="muted">Checking…</p>}</div>
          : <div>
            <p><b>{check.fileName}</b> · {check.project.name} · {Object.entries(check.counts).map(([k, v]) => `${v} ${k.toLowerCase()}`).join(' · ')}</p>
            {check.issues.length > 0 && <div className="gw" style={{ maxHeight: 200 }}><table className="dg" style={{ width: '100%' }}><tbody>{check.issues.map((i, k) => <tr key={k}><td>{i.sheet}</td><td className="n">{i.row ?? '–'}</td><td><span className={`tag ${i.severity === 'error' ? 'r' : 'y'}`}>{i.severity}</span></td><td>{i.message}</td></tr>)}</tbody></table></div>}
            <div className="gw" style={{ maxHeight: 260, marginTop: 10 }}><table className="dg" style={{ width: '100%' }}><thead><tr><th>CSI</th><th>Trade</th><th className="r">Budget</th><th className="r">Bidders</th><th>Match</th></tr></thead>
              <tbody>{check.trades.map(t => <tr key={t.code}><td className="code">{t.code}</td><td>{t.title}</td><td className="n">{money(t.budget)}</td><td className="n">{t.bidders}</td><td><span className={`tag ${t.match === 'New' ? 'o' : 'g'}`}>{t.match}</span></td></tr>)}</tbody></table></div>
            <p className="muted">{check.partners.matched} trade partners matched · {check.partners.created} will be added.</p>
            <button className="btn g" onClick={() => setCheck(null)}>Upload another file</button>
          </div>)
        : <div className="fgrid">
            {([['projectName', 'Project name'], ['clientName', 'Client'], ['city', 'City'], ['stateCode', 'State'], ['projectType', 'Project type'], ['bidDueOn', 'Bid due']] as const).map(([k, l]) =>
              <div className="field" key={k}><label>{l}</label><input className="inp" type={k === 'bidDueOn' ? 'date' : 'text'} value={form[k]} onChange={e => setForm({ ...form, [k]: e.target.value })} /></div>)}
          </div>}
        {err && <p role="alert" style={{ color: 'var(--bad, #d1344b)' }}>{err}</p>}
      </div>
      <div className="mf"><button className="btn" onClick={onClose}>Cancel</button><span style={{ flex: 1 }} />
        <button className="btn p" disabled={busy || (mode === 'import' ? !check?.ok : !form.projectName)} onClick={create}><Icon name="check" />Create project</button></div>
    </div></div>
  )
}
