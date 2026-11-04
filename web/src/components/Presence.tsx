// Who's working on this project right now (like SharePoint's "who's here"). Click a person to jump to
// the page, version and row they're on.
import { useEffect, useRef, useState } from 'react'
import { useLocation, useNavigate } from 'react-router'
import { onPresence } from '../live/hub'
import { useAuth } from '../app/auth'
import { tabId } from '../api/client'
import type { PresenceRow } from '../api/types'
import { Icon } from './Icon'

import { PROJECT_PAGES } from '../app/pages'
const PAGE_NAMES: Record<string, string> = Object.fromEntries(PROJECT_PAGES.map(p => [p.key, p.label]))

/** Everyone on this project right now (every tab counts, including your own other tabs). */
export function usePresence() {
  const [rows, setRows] = useState<PresenceRow[]>([])
  useEffect(() => onPresence(setRows), [])
  return rows
}
/** versionId = the version this tab has open: people working in another version of the project are not shown. */
export interface PresenceProject { no: string; versionId?: number; versions: { versionId: number; versionNo: number }[] }

/** Where a presence row points: /p/{no}/v/{versionNo}/{page}?{query}&row={lineId}. */
export function presenceHref(r: PresenceRow, p: PresenceProject) {
  const v = p.versions.find(x => x.versionId === r.versionId)
  if (!v || !r.pageKey) return null
  const [page, query = ''] = r.pageKey.split('?')
  const qs = new URLSearchParams(query)
  if (r.focusKind === 'answer' && r.focusKey) qs.set('row', r.focusKey.split(':')[0])
  const q = qs.toString()
  return `/p/${encodeURIComponent(p.no)}/v/${v.versionNo}/${page}${q ? '?' + q : ''}`
}

export function Presence({ project }: { project?: PresenceProject }) {
  const rows = usePresence(); const { me } = useAuth(); const [open, setOpen] = useState(false)
  const nav = useNavigate(); const loc = useLocation(); const ref = useRef<HTMLDivElement>(null)
  useEffect(() => {
    if (!open) return
    const c = (e: MouseEvent) => { if (!ref.current?.contains(e.target as Node)) setOpen(false) }
    const k = (e: KeyboardEvent) => e.key === 'Escape' && setOpen(false)
    addEventListener('mousedown', c); addEventListener('keydown', k)
    return () => { removeEventListener('mousedown', c); removeEventListener('keydown', k) }
  }, [open])
  const others = rows.filter(r => !(r.userId === me?.userId && r.connectionId.endsWith(tabId)) && (project?.versionId == null || r.versionId === project.versionId))
  const people = [...new Map(others.map(r => [r.userId, r])).values()]
  const here = loc.pathname + loc.search
  function go(r: PresenceRow) {
    const href = project && presenceHref(r, project)
    if (!href) return
    setOpen(false); nav(href)
  }
  return (
    <div className="presence-wrap" ref={ref}>
      <button className="presence" onClick={() => setOpen(o => !o)} title="Who's working on this version of the project" aria-expanded={open} aria-haspopup="dialog">
        {people.length ? <>{people.slice(0, 4).map(p => <span key={p.userId} className="pa" style={{ background: p.colour }}>{p.initials}</span>)}<span className="more">{people.length} here</span></>
          : <span className="more">Only you</span>}
      </button>
      {open && <div className="whohere" role="dialog" aria-label="Working on this project">
        <div className="ch"><h3>Working on this version</h3><small className="mut">Click a person to go to where they are</small></div>
        <div className="list">{others.length ? others.map(r => {
          const href = project ? presenceHref(r, project) : null
          const v = project?.versions.find(x => x.versionId === r.versionId)
          const page = (r.pageKey ?? '').split('?')[0]
          const sameSpot = href != null && href.split('&row=')[0].split('?row=')[0] === here.split('&row=')[0].split('?row=')[0]
          return <button className="li go" key={r.connectionId} disabled={!href} onClick={() => go(r)} title={href ? `Go to ${PAGE_NAMES[page] ?? page}` : 'Not on a project page'}>
            <span className="pa" style={{ background: r.colour }}>{r.initials}</span>
            <div className="t"><b>{r.personName}{r.userId === me?.userId ? ' (you, another tab)' : ''}</b>
              <small>{PAGE_NAMES[page] ?? (page || 'Opening…')}{v ? ` · v${v.versionNo}` : ''} · {r.activity}{sameSpot ? ' · same page as you' : ''}</small></div>
            {href && <Icon name="chevr" size="s" />}
          </button>
        }) : <div className="li"><div className="t"><small>Nobody else is here. Open the project in another tab or as another user to see presence.</small></div></div>}</div>
      </div>}
    </div>
  )
}
