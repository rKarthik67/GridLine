import { useEffect, useRef, useState, type ReactNode } from 'react'
import { useIsFetching, useQueryClient } from '@tanstack/react-query'
import { NavLink, useNavigate, useParams } from 'react-router'
import { useAuth } from '../app/auth'
import { Icon } from './Icon'
import { Presence } from './Presence'
import { debugEnabled, setDebug } from '../api/debug'

export { PROJECT_PAGES } from '../app/pages'
import { PAGE_GROUPS, PROJECT_PAGES } from '../app/pages'

export function Shell({ title, crumbs, children, project }: { title: string; crumbs?: ReactNode; children: ReactNode; project?: { no: string; name: string; versionNo: number; stage: string; versions?: { versionId: number; versionNo: number }[] } }) {
  const nav = useNavigate(); const [collapsed, setCollapsed] = useState(false)
  const { versionNo } = useParams()
  useEffect(() => { document.title = `${title}${project ? ' · ' + project.name : ''} · Gridline` }, [title, project])
  const base = project ? `/p/${encodeURIComponent(project.no)}/v/${versionNo ?? project.versionNo}` : ''
  return (
    <div className={`app ${collapsed ? 'collapsed' : ''}`}>
      <aside className="side" aria-label="Main navigation">
        <div className="brand"><span className="mark">G</span><b>Gridline<i>.</i></b></div>
        {project && (
          <button className="pswitch" onClick={() => nav('/projects')} title="Switch project">
            <span className="pi"><Icon name="build" /></span>
            <span className="pt"><small>Current project</small><b>{project.name}</b><span><span className="chip stage">{project.stage}</span><span className="chip ver">v{project.versionNo}</span></span></span>
            <Icon name="updown" size="s" />
          </button>
        )}
        <nav className="nav">
          <div className="ng"><div className="nh"><span>My desk</span></div><div className="nl">
            <NavLink to="/projects" end data-was="Projects"><Icon name="folder" /><span>Portfolio</span></NavLink>
            <NavLink to="/projects/new" data-was="New project"><Icon name="plus" /><span>Start a project</span></NavLink>
          </div></div>
          {project && PAGE_GROUPS.map(g => <div className="ng" key={g.key}><div className="nh">{g.key === 'project' && <Icon name="build" size="s" />}<span>{g.key === 'project' ? project.name : g.label}</span></div><div className="nl">
            {PROJECT_PAGES.filter(p => p.group === g.key).map(p => <NavLink key={p.key} to={`${base}/${p.key}`} data-was={p.was}><Icon name={p.icon} /><span>{p.label}</span></NavLink>)}
          </div></div>)}
        </nav>
        <div className="sidefoot"><button onClick={() => setCollapsed(c => !c)}><Icon name="side" /><span>Collapse sidebar</span></button></div>
      </aside>
      <div className="main">
        <header className="top">
          <div className="crumb">{crumbs ?? <b>{title}</b>}</div>
          <Presence project={project ? { no: project.no, versions: project.versions ?? [], versionId: project.versions?.find(v => v.versionNo === Number(versionNo ?? project.versionNo))?.versionId } : undefined} />
          <button className="tbtn" title="Open this page in a new tab" onClick={() => window.open(location.href, '_blank', 'noopener')}><Icon name="expand" /></button>
          <ProfileMenu />
        </header>
        <main className="view"><div className="page">{children}</div></main>
      </div>
    </div>
  )
}
/** Your profile: who you're signed in as, your roles, and an explicit Sign out (clicking the avatar no longer signs you out). */
function ProfileMenu() {
  const { me, signOut } = useAuth(); const nav = useNavigate(); const [open, setOpen] = useState(false); const ref = useRef<HTMLDivElement>(null)
  const [dbg, setDbg] = useState(() => debugEnabled())
  useEffect(() => {
    if (!open) return
    const c = (e: MouseEvent) => { if (!ref.current?.contains(e.target as Node)) setOpen(false) }
    const k = (e: KeyboardEvent) => e.key === 'Escape' && setOpen(false)
    addEventListener('mousedown', c); addEventListener('keydown', k)
    return () => { removeEventListener('mousedown', c); removeEventListener('keydown', k) }
  }, [open])
  return <div className="profile-wrap" ref={ref}>
    <button className="avatar" title={me ? `${me.name} · profile` : 'Profile'} aria-haspopup="menu" aria-expanded={open} style={{ background: me?.colour }} onClick={() => setOpen(o => !o)}>{me?.initials ?? '?'}</button>
    {open && <div className="profmenu" role="menu">
      <div className="pm-head"><span className="avatar lg" style={{ background: me?.colour }}>{me?.initials ?? '?'}</span>
        <div><b>{me?.name}</b><small>{me?.email}</small></div></div>
      {!!me?.roles.length && <div className="pm-roles">{me.roles.map(r => <span key={r} className="chip">{r}</span>)}</div>}
      <hr />
      <button role="menuitem" onClick={() => { setOpen(false); nav('/projects') }}><Icon name="folder" size="s" />My portfolio</button>
      <button role="menuitem" onClick={() => window.open(location.href, '_blank', 'noopener')}><Icon name="expand" size="s" />Open this page in a new tab</button>
      <button role="menuitem" onClick={() => { const v = !dbg; setDebug(v); setDbg(v) }}><Icon name="gear" size="s" />Debug logging in console: {dbg ? 'on' : 'off'}</button>
      <hr />
      <button role="menuitem" className="danger" onClick={() => { setOpen(false); signOut().then(() => nav('/signin')) }}><Icon name="logout" size="s" />Sign out</button>
    </div>}
  </div>
}
/** Reloads everything on the page from the server (like the original's Refresh buttons). */
export function RefreshButton() {
  const qc = useQueryClient(); const busy = useIsFetching() > 0
  return <button className="btn" onClick={() => qc.invalidateQueries()} disabled={busy} title="Reload this page's data from the server">
    <Icon name="sync" />{busy ? 'Refreshing…' : 'Refresh'}</button>
}
export function PageHead({ eyebrow, title, desc, actions, was, noRefresh }: { eyebrow?: ReactNode; title: string; desc?: string; actions?: ReactNode; was?: string; noRefresh?: boolean }) {
  return <div className="ph" data-was={was}><div>{eyebrow && <div className="eb">{eyebrow}</div>}<h1>{title}</h1>{desc && <p>{desc}</p>}</div><div className="acts">{!noRefresh && <RefreshButton />}{actions}</div></div>
}
