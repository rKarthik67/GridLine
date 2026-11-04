import { useMemo, useState } from 'react'
import { useNavigate } from 'react-router'
import { useQuery } from '@tanstack/react-query'
import { api } from '../api/client'
import type { ProjectRow, Stage } from '../api/types'
import { Shell, PageHead } from '../components/Shell'
import { Icon } from '../components/Icon'
import { cap, date, money } from '../app/format'
import { useToast } from '../app/toast'
import StartProject from './StartProject'

const STAGES: (Stage | 'all')[] = ['all', 'lead', 'pursuit', 'bidding', 'submitted', 'awarded', 'lost']
export default function Portfolio({ startNew }: { startNew?: boolean }) {
  const nav = useNavigate(); const toast = useToast()
  const [stage, setStage] = useState<Stage | 'all'>('all'); const [q, setQ] = useState('')
  const { data, isLoading, error, refetch } = useQuery({ queryKey: ['projects'], queryFn: () => api.get<ProjectRow[]>('/projects') })
  const rows = useMemo(() => (data ?? []).filter(p => (stage === 'all' || p.stage === stage) &&
    (!q || `${p.projectName} ${p.projectNo} ${p.city ?? ''} ${p.clientName ?? ''}`.toLowerCase().includes(q.toLowerCase()))), [data, stage, q])
  const open = (p: ProjectRow, newTab = false) => { const url = `/p/${encodeURIComponent(p.projectNo)}/v/${p.primaryVersionNo ?? 1}/bid-board`; newTab ? window.open(url, '_blank') : nav(url) }
  async function fav(p: ProjectRow) { await api.put(`/projects/${p.projectId}/favourite`, { isFavourite: !p.isFavourite }); refetch() }
  async function exportList() { try { toast('Exported ' + await api.download('/exports/portfolio.xlsx', 'Portfolio.xlsx'), 'down') } catch (e) { toast((e as Error).message, 'alert') } }
  return (
    <Shell title="Portfolio">
      <PageHead eyebrow="My desk" title="Portfolio" desc="Every project, its stage and where pricing stands. Ctrl/⌘-click or the ↗ button opens a project in a new tab."
        actions={<><button className="btn" onClick={exportList}><Icon name="down" />Export</button><button className="btn p" onClick={() => nav('/projects/new')}><Icon name="plus" />Start a project</button></>} />
      <div className="card">
        <div className="tabs" role="tablist">{STAGES.map(s => <button key={s} role="tab" aria-selected={stage === s} onClick={() => setStage(s)}>{s === 'all' ? 'All' : cap(s)}<span className="n">{s === 'all' ? data?.length ?? 0 : data?.filter(p => p.stage === s).length ?? 0}</span></button>)}</div>
        <div className="tb"><label className="sbox"><Icon name="search" size="s" /><input placeholder="Search name, number, client or city" value={q} onChange={e => setQ(e.target.value)} style={{ width: 300 }} /></label></div>
        {isLoading ? <div className="empty">Loading projects…</div> : error ? <div className="empty">Couldn't load projects: {(error as Error).message}</div> :
        <div className="gw"><table className="dg" style={{ width: '100%' }}>
          <thead><tr><th style={{ width: 34 }}></th><th>Project</th><th>Stage</th><th>Client</th><th>Bid due</th><th className="r">Trades</th><th className="r">Contract total</th><th>Estimator</th><th style={{ width: 70 }}></th></tr></thead>
          <tbody>{rows.map(p => <tr key={p.projectId}>
            <td><button className="rm" style={{ opacity: 1, color: p.isFavourite ? 'var(--accent)' : 'var(--faint)' }} onClick={() => fav(p)} aria-label="Favourite"><Icon name="star" size="s" /></button></td>
            <td><a className="lnk" href={`/p/${encodeURIComponent(p.projectNo)}/v/${p.primaryVersionNo ?? 1}/bid-board`} onClick={e => { if (!e.ctrlKey && !e.metaKey) { e.preventDefault(); open(p) } }}>{p.projectName}</a>
              <div className="code">{p.projectNo} · {[p.city, p.stateCode].filter(Boolean).join(', ')}{p.peopleHere ? ` · ${p.peopleHere} working now` : ''}</div></td>
            <td><span className={`tag ${p.stage === 'awarded' ? 'g' : p.stage === 'lost' ? 'r' : 'b'}`}>{cap(p.stage)}</span></td>
            <td>{p.clientName}</td><td className="num">{date(p.bidDueOn)}</td>
            <td className="n">{p.tradesCarried}/{p.trades}</td><td className="n">{money(p.contractTotal ?? p.tradesTotal)}</td><td>{p.estimator}</td>
            <td><button className="btn sm g ic" title="Open in a new tab" onClick={() => open(p, true)}><Icon name="expand" size="s" /></button></td>
          </tr>)}</tbody>
        </table>{!rows.length && <div className="empty">No projects match.</div>}</div>}
      </div>
      {startNew && <StartProject onClose={() => nav('/projects')} onCreated={(no, v) => { refetch(); nav(`/p/${encodeURIComponent(no)}/v/${v}/bid-board`) }} />}
    </Shell>
  )
}
