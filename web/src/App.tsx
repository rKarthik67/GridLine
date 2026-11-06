import type { ReactNode } from 'react'
import { Navigate, Route, Routes, useLocation } from 'react-router'
import { useAuth } from './app/auth'
import SignIn from './pages/auth/SignIn'
import SignUp from './pages/auth/SignUp'
import Forgot from './pages/auth/Forgot'
import Reset from './pages/auth/Reset'
import Invite from './pages/auth/Invite'
import Portfolio from './pages/Portfolio'
import ProjectLayout from './pages/ProjectLayout'
import Portal from './pages/Portal'
import { lazy, Suspense } from 'react'
const ScratchWindow = lazy(() => import('./pages/ScratchWindow'))
const Admin = lazy(() => import('./pages/admin/Admin'))
const Account = lazy(() => import('./pages/Account'))

function Private({ children, admin }: { children: ReactNode; admin?: boolean }) {
  const { me, loading, isAdmin } = useAuth(); const loc = useLocation()
  if (loading) return <div className="empty" style={{ padding: 40 }}>Signing you in…</div>
  if (!me) return <Navigate to={`/signin?next=${encodeURIComponent(loc.pathname + loc.search)}`} replace />
  if (admin && !isAdmin) return <Navigate to="/projects" replace />
  return <>{children}</>
}
const lazyPage = (el: ReactNode) => <Suspense fallback={<div className="empty" style={{ padding: 40 }}>Loading…</div>}>{el}</Suspense>

export default function App() {
  return (
    <Routes>
      <Route path="/signin" element={<SignIn />} />
      <Route path="/signup" element={<SignUp />} />
      <Route path="/forgot" element={<Forgot />} />
      <Route path="/reset/:token" element={<Reset />} />
      <Route path="/invite/:token" element={<Invite />} />
      <Route path="/bid/:token" element={<Portal />} />
      <Route path="/projects" element={<Private><Portfolio /></Private>} />
      <Route path="/projects/new" element={<Private><Portfolio startNew /></Private>} />
      <Route path="/account" element={<Private>{lazyPage(<Account />)}</Private>} />
      <Route path="/admin/:section?" element={<Private admin>{lazyPage(<Admin />)}</Private>} />
      <Route path="/scratch/:vid/:tradeId" element={<Private><Suspense fallback={null}><ScratchWindow /></Suspense></Private>} />
      <Route path="/p/:projectNo/v/:versionNo/:page/*" element={<Private><ProjectLayout /></Private>} />
      <Route path="/p/:projectNo" element={<Private><ProjectLayout /></Private>} />
      <Route path="*" element={<Navigate to="/projects" replace />} />
    </Routes>
  )
}
