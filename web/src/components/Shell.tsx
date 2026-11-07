import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { useIsFetching, useQuery, useQueryClient } from '@tanstack/react-query'
import { NavLink, useNavigate, useParams } from 'react-router'
import { useAuth } from '../app/auth'
import { useConfig } from '../app/config'
import { api } from '../api/client'
import type { ProjectRow } from '../api/types'
import { cap } from '../app/format'
import { Icon } from './Icon'
import { Presence } from './Presence'
import { debugEnabled, setDebug } from '../api/debug'

export { PROJECT_PAGES } from '../app/pages'
import { PAGE_GROUPS, PROJECT_PAGES, pageOn } from '../app/pages'
import { useFeatures } from '../app/hooks'

export const ADMIN_PAGES = [
  { key: 'company', label: 'Company', icon: 'company' },
  { key: 'people', label: 'People & access', icon: 'users' },
  { key: 'partners', label: 'Trade partners', icon: 'people' },
  { key: 'staff', label: 'Employees & roles', icon: 'team' },
  { key: 'templates', label: 'Template library', icon: 'book' },
  { key: 'audit', label: 'Audit trail', icon: 'clock' },
] as const
export type AdminKey = typeof ADMIN_PAGES[number]['key']
type ShellProject = { no: string; name: string; versionNo: number; stage: string; versions?: { versionId: number; versionNo: number }[] }

export function Shell({ title, crumbs, children, project }: { title: string; crumbs?: ReactNode; children: ReactNode; project?: ShellProject }) {
  const nav = useNavigate(); const { me, isAdmin } = useAuth(); const feats = useFeatures().data
  const [collapsed, setCollapsed] = useState(() => { try { return localStorage.getItem('gridline.side') === 'collapsed' } catch { return false } })
  const [mobileOpen, setMobileOpen] = useState(false); const [palette, setPalette] = useState(false)
  const { versionNo } = useParams()
  useEffect(() => { document.title = `${title}${project ? ' · ' + project.name : ''} · Gridline` }, [title, project])
  useEffect(() => { try { localStorage.setItem('gridline.side', collapsed ? 'collapsed' : 'open') } catch { /* private mode */ } }, [collapsed])
  useEffect(() => {
    const k = (e: KeyboardEvent) => { if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') { e.preventDefault(); setPalette(p => !p) } }
    addEventListener('keydown', k); return () => removeEventListener('keydown', k)
  }, [])
  const base = project ? `/p/${encodeURIComponent(project.no)}/v/${versionNo ?? project.versionNo}` : ''
  const trialDays = me?.company?.plan === 'trial' && me.company.trialEndsOn ? Math.ceil((Date.parse(me.company.trialEndsOn) - Date.now()) / 864e5) : null
  const closeMobile = () => setMobileOpen(false)
  return (
    <div className={`app ${collapsed ? 'collapsed' : ''} ${mobileOpen ? 'mopen' : ''}`}>
      {mobileOpen && <div className="mscrim" onClick={closeMobile} />}
      <aside className="side" aria-label="Main navigation">
        <div className="brand" title={me?.company?.name}><span className="mark">G</span><span style={{ minWidth: 0 }}><b>Gridline<i>.</i></b>{me?.company?.name && <span className="brand-co">{me.company.name}</span>}</span>
          <button className="tbtn mclose" onClick={closeMobile} aria-label="Close menu"><Icon name="x" /></button></div>
        {project && (
          <button className="pswitch" onClick={() => { closeMobile(); setPalette(true) }} title="Switch project (Ctrl+K)">
            <span className="pi"><Icon name="build" /></span>
            <span className="pt"><small>Current project</small><b>{project.name}</b><span><span className="chip stage">{project.stage}</span><span className="chip ver">v{project.versionNo}</span></span></span>
            <Icon name="updown" size="s" />
          </button>
        )}
        <nav className="nav" onClick={e => { if ((e.target as HTMLElement).closest('a')) closeMobile() }}>
          <div className="ng"><div className="nh"><span>My desk</span></div><div className="nl">
            <NavLink to="/projects" end data-was="Projects"><Icon name="folder" /><span>Portfolio</span></NavLink>
            <NavLink to="/projects/new" data-was="New project"><Icon name="plus" /><span>Start a project</span></NavLink>
          </div></div>
          {project && PAGE_GROUPS.map(g => <div className="ng" key={g.key}><div className="nh">{g.key === 'project' && <Icon name="build" size="s" />}<span>{g.key === 'project' ? project.name : g.label}</span></div><div className="nl">
            {PROJECT_PAGES.filter(p => p.group === g.key && pageOn(p, feats as unknown as Record<string, boolean>)).map(p => <NavLink key={p.key} to={`${base}/${p.key}`} data-was={p.was}><Icon name={p.icon} /><span>{p.label}</span></NavLink>)}
          </div></div>)}
          {isAdmin && !project && <div className="ng"><div className="nh"><span>Administration</span></div><div className="nl">
            {ADMIN_PAGES.map(p => <NavLink key={p.key} to={`/admin/${p.key}`}><Icon name={p.icon} /><span>{p.label}</span></NavLink>)}
          </div></div>}
        </nav>
        <div className="sidefoot">
          {isAdmin && project && <NavLink to="/admin/company" onClick={closeMobile}><Icon name="gear" /><span>Administration</span></NavLink>}
          <button onClick={() => setCollapsed(c => !c)} className="desk-only"><Icon name="side" /><span>Collapse sidebar</span></button>
        </div>
      </aside>
      <div className="main">
        <header className="top">
          <button className="tbtn mtoggle" onClick={() => setMobileOpen(true)} aria-label="Open menu"><Icon name="menu" /></button>
          <div className="crumb">{crumbs ?? <b>{title}</b>}</div>
          <button className="search" onClick={() => setPalette(true)} title="Jump to a project or page (Ctrl+K)"><Icon name="search" size="s" /><span>Jump to a project or page…</span><kbd>Ctrl K</kbd></button>
          {trialDays != null && <button className={`env trial ${trialDays <= 5 ? 'soon' : ''}`} onClick={() => nav('/admin/company')} title="Your trial plan">{trialDays > 0 ? `Trial · ${trialDays} day${trialDays === 1 ? '' : 's'} left` : 'Trial ended'}</button>}
          <Presence project={project ? { no: project.no, versions: project.versions ?? [], versionId: project.versions?.find(v => v.versionNo === Number(versionNo ?? project.versionNo))?.versionId } : undefined} />
          <button className="tbtn desk-only" title="Open this page in a new tab" onClick={() => window.open(location.href, '_blank', 'noopener')}><Icon name="expand" /></button>
          <ProfileMenu />
        </header>
        <main className="view"><div className="page">{children}</div></main>
      </div>
      {palette && <Palette project={project} onClose={() => setPalette(false)} />}
    </div>
  )
}

/** Ctrl+K: jump to any project, page or admin section by typing a few letters. */
function Palette({ project, onClose }: { project?: ShellProject; onClose(): void }) {
  const nav = useNavigate(); const { isAdmin } = useAuth(); const { versionNo } = useParams(); const feats = useFeatures().data
  const [q, setQ] = useState(''); const [cur, setCur] = useState(0); const ref = useRef<HTMLInputElement>(null)
  const projects = useQuery({ queryKey: ['projects'], queryFn: () => api.get<ProjectRow[]>('/projects'), staleTime: 60_000 })
  type Item = { kind: 'project' | 'page' | 'admin'; label: string; hint: string; icon: string; to: string }
  const items = useMemo<Item[]>(() => {
    const base = project ? `/p/${encodeURIComponent(project.no)}/v/${versionNo ?? project.versionNo}` : ''
    const pages: Item[] = project ? PROJECT_PAGES.filter(p => pageOn(p, feats as unknown as Record<string, boolean>)).map(p => ({ kind: 'page', label: p.label, hint: project.name, icon: p.icon, to: `${base}/${p.key}` })) : []
    const desk: Item[] = [{ kind: 'page', label: 'Portfolio', hint: 'My desk', icon: 'folder', to: '/projects' }, { kind: 'page', label: 'Start a project', hint: 'My desk', icon: 'plus', to: '/projects/new' }, { kind: 'page', label: 'Account & password', hint: 'My desk', icon: 'team', to: '/account' }]
    const admin: Item[] = isAdmin ? ADMIN_PAGES.map(p => ({ kind: 'admin', label: p.label, hint: 'Administration', icon: p.icon, to: `/admin/${p.key}` })) : []
    const projs: Item[] = (projects.data ?? []).map(p => ({ kind: 'project', label: p.projectName, hint: `${p.projectNo} · ${cap(p.stage)}${p.clientName ? ' · ' + p.clientName : ''}`, icon: 'build', to: `/p/${encodeURIComponent(p.projectNo)}/v/${p.primaryVersionNo ?? 1}/bid-board` }))
    const all = [...pages, ...projs, ...desk, ...admin]
    const t = q.trim().toLowerCase(); if (!t) return all.slice(0, 14)
    const words = t.split(/\s+/)
    return all.filter(i => words.every(w => `${i.label} ${i.hint}`.toLowerCase().includes(w))).slice(0, 14)
  }, [q, projects.data, project, versionNo, isAdmin])
  useEffect(() => { ref.current?.focus() }, []); useEffect(() => setCur(0), [q])
  const go = (i: Item) => { onClose(); nav(i.to) }
  return <div className="scrim" onMouseDown={onClose}><div className="cmdk" role="dialog" aria-label="Jump to" onMouseDown={e => e.stopPropagation()}>
    <input ref={ref} value={q} placeholder="Type a project, page or trade partner name…" onChange={e => setQ(e.target.value)}
      onKeyDown={e => { if (e.key === 'ArrowDown') { e.preventDefault(); setCur(c => Math.min(items.length - 1, c + 1)) } else if (e.key === 'ArrowUp') { e.preventDefault(); setCur(c => Math.max(0, c - 1)) } else if (e.key === 'Enter' && items[cur]) go(items[cur]); else if (e.key === 'Escape') onClose() }} />
    <div className="res">{items.map((i, n) => <button key={i.to + i.label} className={n === cur ? 'on' : ''} onMouseEnter={() => setCur(n)} onClick={() => go(i)}><Icon name={i.icon} size="s" /><b>{i.label}</b><small>{i.hint}</small></button>)}
      {!items.length && <div className="empty" style={{ padding: 20 }}>Nothing matches.</div>}</div>
    <div className="ft"><span>↑↓ move</span><span>↵ open</span><span>esc close</span><span style={{ marginLeft: 'auto' }}>Ctrl K from anywhere</span></div>
  </div></div>
}

/** Your profile: who you're signed in as, your roles, account settings, help, and an explicit Sign out. */
function ProfileMenu() {
  const { me, signOut, isAdmin } = useAuth(); const nav = useNavigate(); const [open, setOpen] = useState(false); const ref = useRef<HTMLDivElement>(null)
  const [dbg, setDbg] = useState(() => debugEnabled()); const cfg = useConfig()
  useEffect(() => {
    if (!open) return
    const c = (e: MouseEvent) => { if (!ref.current?.contains(e.target as Node)) setOpen(false) }
    const k = (e: KeyboardEvent) => e.key === 'Escape' && setOpen(false)
    addEventListener('mousedown', c); addEventListener('keydown', k)
    return () => { removeEventListener('mousedown', c); removeEventListener('keydown', k) }
  }, [open])
  const support = cfg.data?.supportEmail
  return <div className="profile-wrap" ref={ref}>
    <button className="avatar" title={me ? `${me.name} · profile` : 'Profile'} aria-haspopup="menu" aria-expanded={open} style={{ background: me?.colour, color: '#fff' }} onClick={() => setOpen(o => !o)}>{me?.initials ?? '?'}</button>
    {open && <div className="profmenu" role="menu">
      <div className="pm-head"><span className="avatar lg" style={{ background: me?.colour, color: '#fff' }}>{me?.initials ?? '?'}</span>
        <div><b>{me?.name}</b><small>{me?.email}</small><small>{me?.company?.name}</small></div></div>
      {!!me?.roles.length && <div className="pm-roles">{me.roles.map(r => <span key={r} className="chip">{r}</span>)}</div>}
      <hr />
      <button role="menuitem" onClick={() => { setOpen(false); nav('/projects') }}><Icon name="folder" size="s" />My portfolio</button>
      <button role="menuitem" onClick={() => { setOpen(false); nav('/account') }}><Icon name="team" size="s" />Account &amp; password</button>
      {isAdmin && <button role="menuitem" onClick={() => { setOpen(false); nav('/admin/company') }}><Icon name="gear" size="s" />Company administration</button>}
      <button role="menuitem" onClick={() => window.open(location.href, '_blank', 'noopener')}><Icon name="expand" size="s" />Open this page in a new tab</button>
      <hr />
      {support ? <a role="menuitem" href={`mailto:${support}?subject=${encodeURIComponent('Gridline help · ' + (me?.company?.name ?? ''))}`}><Icon name="help" size="s" />Help &amp; support</a>
        : <button role="menuitem" onClick={() => { setOpen(false); nav('/admin/company') }}><Icon name="help" size="s" />Help &amp; support</button>}
      <button role="menuitem" onClick={() => { const v = !dbg; setDebug(v); setDbg(v) }}><Icon name="flask" size="s" />Debug logging in console: {dbg ? 'on' : 'off'}</button>
      {cfg.data?.version && <div className="pm-ver">Gridline {cfg.data.version}</div>}
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
