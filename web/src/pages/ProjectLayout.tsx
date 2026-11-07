// Loads the project named in the address (/p/:projectNo/v/:versionNo/:page). Each tab has its own address,
// so each tab works on its own project and version, and joins presence for exactly that page.
import { lazy, Suspense, useEffect } from 'react'
import { Navigate, useParams, useSearchParams } from 'react-router'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import type { ProjectDetail, Version } from '../api/types'
import { Shell, PROJECT_PAGES } from '../components/Shell'
import { join, leave, onChange } from '../live/hub'
import { useToast } from '../app/toast'
import { cap } from '../app/format'
import { describe } from '../app/verbs'
import { ErrorBoundary } from '../components/ErrorBoundary'

const PAGES: Record<string, React.LazyExoticComponent<React.ComponentType<{ project: ProjectDetail; version: Version; pageKey: string }>>> = {
  profile: lazy(() => import('./project/Profile')),
  'bid-board': lazy(() => import('./project/BidBoard')),
  'scope-match': lazy(() => import('./project/ScopeMatch')),
  'base-requirements': lazy(() => import('./project/BaseRequirements')),
  'price-roll-up': lazy(() => import('./project/PriceRollUp')),
  'crew-plan': lazy(() => import('./project/CrewPlan')),
  'precon-services': lazy(() => import('./project/Costs')),
  overheads: lazy(() => import('./project/Costs')),
  alternates: lazy(() => import('./project/Alternates')),
  allowances: lazy(() => import('./project/Allowances')),
  'unit-prices': lazy(() => import('./project/UnitPrices')),
}
export default function ProjectLayout() {
  const { projectNo = '', versionNo, page } = useParams(); const qc = useQueryClient(); const toast = useToast()
  const { data: project, error, isLoading } = useQuery({ queryKey: ['project', projectNo], queryFn: () => api.get<ProjectDetail>(`/projects/${encodeURIComponent(projectNo)}`) })
  const version = project?.versions.find(v => String(v.versionNo) === versionNo) ?? null
  // which trade (etc.) you're on goes into presence, so a teammate who clicks you lands on the same trade
  const [sp] = useSearchParams(); const pageQuery = sp.get('trade') ? `trade=${sp.get('trade')}` : ''
  useEffect(() => {
    if (!project || !version || !page) return
    join(project.projectId, version.versionId, page + (pageQuery ? '?' + pageQuery : ''))
    const off = onChange(e => {
      qc.invalidateQueries({ predicate: q => JSON.stringify(q.queryKey).includes(`"v${e.versionId ?? version.versionId}"`) || q.queryKey[0] === 'project' || q.queryKey[0] === 'crew' })   // the crew plan is shared by every version
      toast(`${e.personName} ${describe(e.verb)}`, 'sync')
    })
    return () => { off(); leave() }
  }, [project?.projectId, version?.versionId, page, pageQuery])
  if (isLoading) return <div className="empty" style={{ padding: 40 }}>Opening {projectNo}…</div>
  if (error || !project) return <div className="empty" style={{ padding: 40 }}>Couldn't open {projectNo}: {(error as Error)?.message ?? 'not found'}</div>
  const primary = project.versions.find(v => v.isPrimary) ?? project.versions[0]
  if (!version || !page) return <Navigate to={`/p/${encodeURIComponent(projectNo)}/v/${primary.versionNo}/${page ?? 'bid-board'}`} replace />
  const Page = PAGES[page]; const meta = PROJECT_PAGES.find(p => p.key === page)
  if (!Page || !meta) return <Navigate to={`/p/${encodeURIComponent(projectNo)}/v/${version.versionNo}/bid-board`} replace />
  return (
    <Shell title={meta.label} project={{ no: project.projectNo, name: project.projectName, versionNo: version.versionNo, stage: cap(project.stage), versions: project.versions }}
      crumbs={<><span>{project.projectName}</span><span>›</span><b>{meta.label}</b></>}>
      <ErrorBoundary key={page} where={meta.label}><Suspense fallback={<div className="empty">Loading…</div>}><Page key={page} project={project} version={version} pageKey={page} /></Suspense></ErrorBoundary>
    </Shell>
  )
}
