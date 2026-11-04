import type { ReactNode } from 'react'
import { Navigate, Route, Routes, useLocation } from 'react-router'
import { useAuth } from './app/auth'
import SignIn from './pages/SignIn'
import Portfolio from './pages/Portfolio'
import ProjectLayout from './pages/ProjectLayout'
import Portal from './pages/Portal'
import { lazy, Suspense } from 'react'
const ScratchWindow = lazy(() => import('./pages/ScratchWindow'))

function Private({ children }: { children: ReactNode }) {
  const { me, loading } = useAuth(); const loc = useLocation()
  if (loading) return <div className="empty" style={{ padding: 40 }}>Signing you in…</div>
  if (!me) return <Navigate to={`/signin?next=${encodeURIComponent(loc.pathname + loc.search)}`} replace />
  return <>{children}</>
}
export default function App() {
  return (
    <Routes>
      <Route path="/signin" element={<SignIn />} />
      <Route path="/bid/:token" element={<Portal />} />
      <Route path="/projects" element={<Private><Portfolio /></Private>} />
      <Route path="/projects/new" element={<Private><Portfolio startNew /></Private>} />
      <Route path="/scratch/:vid/:tradeId" element={<Private><Suspense fallback={null}><ScratchWindow /></Suspense></Private>} />
      <Route path="/p/:projectNo/v/:versionNo/:page/*" element={<Private><ProjectLayout /></Private>} />
      <Route path="/p/:projectNo" element={<Private><ProjectLayout /></Private>} />
      <Route path="*" element={<Navigate to="/projects" replace />} />
    </Routes>
  )
}
