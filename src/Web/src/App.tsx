import { QueryClientProvider } from '@tanstack/react-query'
import { BrowserRouter, Link, Navigate, Outlet, Route, Routes } from 'react-router-dom'
import { createQueryClient } from './api/queryClient'
import { currentRole, homePathFor } from './api/session'
import { getTokens } from './api/tokens'
import { PROFILE_KINDS } from './admin/kinds'
import { PushPanel } from './push/PushPanel'
import LoginPage from './pages/LoginPage'
import ProfileListPage from './pages/ProfileListPage'
import ProfileFormPage from './pages/ProfileFormPage'
import ExpertAssignmentsPage from './pages/ExpertAssignmentsPage'
import ExpertAssignmentPage from './pages/ExpertAssignmentPage'

const queryClient = createQueryClient()

function AdminLayout() {
  if (!getTokens()) return <Navigate to="/login" replace />
  return (
    <main>
      <h1>AXA Motor Claims — Admin</h1>
      <nav>
        {PROFILE_KINDS.map((kind) => (
          <span key={kind.slug}>
            <Link to={`/admin/${kind.slug}`}>{kind.label}</Link>{' '}
          </span>
        ))}
      </nav>
      <Outlet />
    </main>
  )
}

function ExpertLayout() {
  if (!getTokens()) return <Navigate to="/login" replace />
  return (
    <main>
      <h1>AXA Motor Claims — Expert</h1>
      {/* In the layout, not on a page: §8's assignment popup is the BRD's primary trigger, and an
          expert who has not enabled it should be offered it on every screen, not only the one they
          happened to land on. AdminLayout slots its <nav> in the same place. */}
      <PushPanel />
      <Outlet />
    </main>
  )
}

function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          {/* Role decides the landing screen; the server decides what each screen may read (§9). */}
          <Route path="/" element={<Navigate to={homePathFor(currentRole())} replace />} />
          <Route element={<ExpertLayout />}>
            <Route path="/expert" element={<ExpertAssignmentsPage />} />
            <Route path="/expert/:id" element={<ExpertAssignmentPage />} />
          </Route>
          <Route element={<AdminLayout />}>
            <Route path="/admin/:kind" element={<ProfileListPage />} />
            <Route path="/admin/:kind/new" element={<ProfileFormPage />} />
            <Route path="/admin/:kind/:id" element={<ProfileFormPage />} />
          </Route>
        </Routes>
      </BrowserRouter>
    </QueryClientProvider>
  )
}

export default App
