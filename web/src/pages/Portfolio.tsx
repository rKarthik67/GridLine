// Portfolio: every project, its stage and where pricing stands, with the pipeline numbers an estimating lead
// looks at first thing: what's due, what's out the door, what we won, and what still has holes.
import { useMemo, useState } from 'react'
import { useNavigate } from 'react-router'
import { useQuery } from '@tanstack/react-query'
import { api } from '../api/client'
import type { ProjectRow, Stage } from '../api/types'
import { Shell, PageHead } from '../components/Shell'
import { Icon } from '../components/Icon'
import { cap, date, money } from '../app/format'
import { useToast } from '../app/toast'
import { useAuth } from '../app/auth'
import StartProject from './StartProject'

const STAGES: (Stage | 'all')[] = ['all', 'lead', 'pursuit', 'bidding', 'submitted', 'awarded', 'lost']
const DAY = 864e5
const daysUntil = (s: string | null) => s ? Math.round((Date.parse(s) - Date.now()) / DAY) : null
const value = (p: ProjectRow) => p.contractTotal ?? p.tradesTotal ?? 0
const short = (n: number) => n >= 1e9 ? `$${(n / 1e9).toFixed(2)}B` : n >= 1e6 ? `$${(n / 1e6).toFixed(1)}M` : n >= 1e3 ? `$${Math.round(n / 1e3)}K` : money(n)

export default function Portfolio({ startNew }: { startNew?: boolean }) {
  const nav = useNavigate(); const toast = useToast(); const { me } = useAuth()
  const [stage, setStage] = useState<Stage | 'all'>('all'); const [q, setQ] = useState('')
  const { data, isLoading, error, refetch } = useQuery({ queryKey: ['projects'], queryFn: () => api.get<ProjectRow[]>('/projects') })
  const rows = useMemo(() => (data ?? []).filter(p => (stage === 'all' || p.stage === stage) &&
    (!q || `${p.projectName} ${p.projectNo} ${p.city ?? ''} ${p.clientName ?? ''}`.toLowerCase().includes(q.toLowerCase()))), [data, stage, q])
  const open = (p: ProjectRow, newTab = false) => { const url = `/p/${encodeURIComponent(p.projectNo)}/v/${p.primaryVersionNo ?? 1}/bid-board`; newTab ? window.open(url, '_blank') : nav(url) }
  async function fav(p: ProjectRow) { await api.put(`/projects/${p.projectId}/favourite`, { isFavourite: !p.isFavourite }); refetch() }
  async function exportList() { try { toast('Exported ' + await api.download('/exports/portfolio.xlsx', 'Portfolio.xlsx'), 'down') } catch (e) { toast((e as Error).message, 'alert') } }

  // ---- the numbers an estimating lead looks at first
  const all = data ?? []
  const live = all.filter(p => p.stage === 'lead' || p.stage === 'pursuit' || p.stage === 'bidding')
  const dueSoon = all.filter(p => (p.stage === 'bidding' || p.stage === 'pursuit') && daysUntil(p.bidDueOn) != null && daysUntil(p.bidDueOn)! <= 14).sort((a, b) => Date.parse(a.bidDueOn!) - Date.parse(b.bidDueOn!))
  const submitted = all.filter(p => p.stage === 'submitted'), awarded = all.filter(p => p.stage === 'awarded'), lost = all.filter(p => p.stage === 'lost')
  const hit = awarded.length + lost.length ? Math.round(awarded.length / (awarded.length + lost.length) * 100) : null
  const holes = all.filter(p => p.stage === 'bidding' && p.trades > p.tradesCarried)

  return (
    <Shell title="Portfolio">
      <PageHead eyebrow={me?.company?.name ?? 'My desk'} title="Portfolio" desc="Every project, its stage and where pricing stands. Ctrl/⌘-click or the ↗ button opens a project in a new tab."
        actions={<><button className="btn" onClick={exportList}><Icon name="down" />Export</button><button className="btn p" onClick={() => nav('/projects/new')}><Icon name="plus" />Start a project</button></>} />
      {all.length > 0 && <div className="dash">
        <div className="kpis" style={{ marginBottom: 0 }}>
          <div className="kpi kdark"><span className="k">Live pipeline</span><span className="v">{short(live.reduce((s, p) => s + value(p), 0))}</span><span className="d">{live.length} lead, pursuit or bidding</span></div>
          <button className="kpi" onClick={() => setStage('bidding')}><span className="k">Due in 14 days</span><span className="v">{dueSoon.length}</span><span className={`d ${dueSoon.some(p => daysUntil(p.bidDueOn)! < 0) ? 'dn' : ''}`}>{dueSoon.some(p => daysUntil(p.bidDueOn)! < 0) ? `${dueSoon.filter(p => daysUntil(p.bidDueOn)! < 0).length} past due` : dueSoon[0] ? `next: ${dueSoon[0].projectName}` : 'nothing due'}</span></button>
          <button className="kpi" onClick={() => setStage('submitted')}><span className="k">Out for decision</span><span className="v">{short(submitted.reduce((s, p) => s + value(p), 0))}</span><span className="d">{submitted.length} submitted</span></button>
          <button className="kpi" onClick={() => setStage('awarded')}><span className="k">Awarded</span><span className="v">{short(awarded.reduce((s, p) => s + value(p), 0))}</span><span className={`d ${hit != null && hit >= 30 ? 'up' : ''}`}>{awarded.length} won{hit != null ? ` · ${hit}% hit rate` : ''}</span></button>
          <button className="kpi" onClick={() => { setStage('bidding'); setQ('') }}><span className="k">Trades unresolved</span><span className="v">{holes.reduce((s, p) => s + (p.trades - p.tradesCarried), 0)}</span><span className={`d ${holes.length ? 'dn' : 'up'}`}>{holes.length ? `across ${holes.length} bidding project${holes.length === 1 ? '' : 's'}` : 'every bidding trade is picked'}</span></button>
        </div>
        {dueSoon.length > 0 && <div className="due"><span className="k"><Icon name="clock" size="s" />Bids due soon</span>
          {dueSoon.slice(0, 6).map(p => { const d = daysUntil(p.bidDueOn)!; return <button key={p.projectId} className={`due-item ${d < 0 ? 'late' : d <= 3 ? 'hot' : ''}`} onClick={() => open(p)} title={`${p.projectNo} · ${p.tradesCarried}/${p.trades} trades picked`}>
            <b>{p.projectName}</b><small>{d < 0 ? `${-d}d past due` : d === 0 ? 'due today' : `in ${d}d`} · {date(p.bidDueOn)} · {p.tradesCarried}/{p.trades} picked</small></button> })}</div>}
      </div>}
      <div className="card">
        <div className="tabs" role="tablist">{STAGES.map(s => <button key={s} role="tab" aria-selected={stage === s} onClick={() => setStage(s)}>{s === 'all' ? 'All' : cap(s)}<span className="n">{s === 'all' ? data?.length ?? 0 : data?.filter(p => p.stage === s).length ?? 0}</span></button>)}</div>
        <div className="tb"><label className="sbox"><Icon name="search" size="s" /><input placeholder="Search name, number, client or city" value={q} onChange={e => setQ(e.target.value)} style={{ width: 300 }} /></label>
          <span className="sp" /><span className="muted" style={{ fontSize: 12 }}>{rows.length} of {all.length} · {short(rows.reduce((s, p) => s + value(p), 0))}</span></div>
        {isLoading ? <div className="empty">Loading projects…</div> : error ? <div className="empty">Couldn't load projects: {(error as Error).message}</div> :
        !all.length ? <div className="empty"><Icon name="build" size="l" /><h3>No projects yet</h3><p>Start one from scratch or import the Excel template with your trades and bidders.</p><button className="btn p" onClick={() => nav('/projects/new')}><Icon name="plus" />Start a project</button></div> :
        <div className="gw"><table className="dg" style={{ width: '100%' }}>
          <thead><tr><th style={{ width: 34 }}></th><th>Project</th><th>Stage</th><th>Client</th><th>Bid due</th><th className="r">Trades picked</th><th className="r">Contract total</th><th className="r">$/SF</th><th>Estimator</th><th style={{ width: 70 }}></th></tr></thead>
          <tbody>{rows.map(p => { const d = daysUntil(p.bidDueOn); const v = value(p)
            return <tr key={p.projectId}>
            <td><button className="rm" style={{ opacity: 1, color: p.isFavourite ? 'var(--accent-d)' : 'var(--faint)' }} onClick={() => fav(p)} aria-label="Favourite"><Icon name="star" size="s" /></button></td>
            <td><a className="lnk" href={`/p/${encodeURIComponent(p.projectNo)}/v/${p.primaryVersionNo ?? 1}/bid-board`} onClick={e => { if (!e.ctrlKey && !e.metaKey) { e.preventDefault(); open(p) } }}>{p.projectName}</a>
              <div className="code">{p.projectNo} · {[p.city, p.stateCode].filter(Boolean).join(', ')}{p.projectType ? ` · ${p.projectType}` : ''}{p.peopleHere ? ` · ${p.peopleHere} working now` : ''}</div></td>
            <td><span className={`tag ${p.stage === 'awarded' ? 'g' : p.stage === 'lost' ? 'r' : p.stage === 'bidding' ? 'o' : 'b'}`}>{cap(p.stage)}</span></td>
            <td>{p.clientName}</td><td className="num">{date(p.bidDueOn)}{d != null && (p.stage === 'bidding' || p.stage === 'pursuit') && <small className={`due-tag ${d < 0 ? 'late' : d <= 7 ? 'hot' : ''}`}>{d < 0 ? `${-d}d late` : d === 0 ? 'today' : `${d}d`}</small>}</td>
            <td className="n"><span className={p.trades && p.tradesCarried < p.trades && p.stage === 'bidding' ? 'warn-txt' : ''}>{p.tradesCarried}/{p.trades}</span></td><td className="n">{money(v)}</td><td className="n mut">{p.grossArea && v ? `$${(v / p.grossArea).toFixed(0)}` : '–'}</td><td>{p.estimator}</td>
            <td><button className="btn sm g ic" title="Open in a new tab" onClick={() => open(p, true)}><Icon name="expand" size="s" /></button></td>
          </tr> })}</tbody>
        </table>{!rows.length && <div className="empty">No projects match.</div>}</div>}
      </div>
      {startNew && <StartProject onClose={() => nav('/projects')} onCreated={(no, v) => { refetch(); nav(`/p/${encodeURIComponent(no)}/v/${v}/bid-board`) }} />}
    </Shell>
  )
}
