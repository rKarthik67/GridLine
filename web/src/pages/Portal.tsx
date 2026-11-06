// Public page a sub opens from the bid link (no login). Shows the trade's scope lines; the sub enters a base bid and answers.
import { useState } from 'react'
import { useParams } from 'react-router'
import { useQuery } from '@tanstack/react-query'
import { api } from '../api/client'

interface PortalData { projectName: string; tradeName: string; csiCode: string; companyName: string; company: string; dueOn: string | null; message: string | null; status: string; lines: { lineId: number; section: string; isHeading: boolean; lineText: string; desired: string | null }[] }
export default function Portal() {
  const { token } = useParams(); const [base, setBase] = useState(''); const [answers, setAnswers] = useState<Record<number, string>>({}); const [notes, setNotes] = useState('')
  const [done, setDone] = useState(false); const [err, setErr] = useState<string | null>(null)
  const { data, error, isLoading } = useQuery({ queryKey: ['portal', token], queryFn: () => api.get<PortalData>(`/portal/${token}`) })
  if (isLoading) return <div className="empty" style={{ padding: 40 }}>Loading your bid form…</div>
  if (error || !data) return <div className="empty" style={{ padding: 40 }}>This bid link is not valid or has expired.</div>
  if (done || data.status === 'submitted') return <div className="card pad" style={{ maxWidth: 560, margin: '60px auto' }}><h2>Thank you</h2><p>Your bid for {data.tradeName} on {data.projectName} has been received.</p></div>
  async function submit() {
    setErr(null)
    try { await api.post(`/portal/${token}`, { baseAmount: Number(base.replace(/[$,]/g, '')) || null, notes, answers: Object.entries(answers).map(([lineId, value]) => ({ lineId: +lineId, value })) }); setDone(true) }
    catch (e) { setErr((e as Error).message) }
  }
  return (
    <div style={{ maxWidth: 860, margin: '30px auto', padding: '0 16px' }}>
      <div className="ph"><div><div className="eb">Bid invitation from {data.company}</div><h1>{data.csiCode ? `${data.csiCode} · ` : ''}{data.tradeName}</h1><p>{data.projectName}{data.dueOn ? ` · due ${data.dueOn}` : ''} · prepared for <b>{data.companyName}</b></p></div>
        <div className="brand" style={{ padding: 0, height: 'auto' }}><span className="mark">G</span><b style={{ color: 'var(--ink)' }}>Gridline<i>.</i></b></div></div>
      {data.message && <div className="card pad" style={{ marginBottom: 12 }}>{data.message}</div>}
      <div className="card"><table className="dg" style={{ width: '100%' }}><thead><tr><th>Scope</th><th>Requested</th><th style={{ width: 220 }}>Your answer</th></tr></thead>
        <tbody>{data.lines.map(l => l.isHeading ? <tr key={l.lineId}><td colSpan={3}><b>{l.lineText}</b></td></tr> :
          <tr key={l.lineId}><td style={{ whiteSpace: 'normal' }}>{l.lineText}</td><td>{l.desired}</td>
            <td><input className="inp" style={{ width: '100%' }} placeholder="Included / Excluded / $ amount" value={answers[l.lineId] ?? ''} onChange={e => setAnswers({ ...answers, [l.lineId]: e.target.value })} /></td></tr>)}</tbody></table></div>
      <div className="card pad" style={{ marginTop: 12 }}>
        <div className="field"><label>Base bid</label><input className="inp num" value={base} onChange={e => setBase(e.target.value)} placeholder="$0" /></div>
        <div className="field" style={{ marginTop: 10 }}><label>Notes and qualifications</label><textarea className="inp" rows={3} value={notes} onChange={e => setNotes(e.target.value)} /></div>
        {err && <p role="alert" style={{ color: 'var(--bad, #d1344b)' }}>{err}</p>}
        <button className="btn p" style={{ marginTop: 12 }} onClick={submit} disabled={!base}>Submit bid</button>
      </div>
    </div>
  )
}
