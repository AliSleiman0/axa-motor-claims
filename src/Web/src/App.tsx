import { QueryClientProvider } from '@tanstack/react-query'
import { BrowserRouter, Link, Navigate, Outlet, Route, Routes } from 'react-router-dom'
import { createQueryClient } from './api/queryClient'
import { currentRole, homePathFor } from './api/session'
import { getTokens } from './api/tokens'
import { PROFILE_KINDS } from './admin/kinds'
import { GARAGE_PUSH_COPY } from './push/copy'
import { PushPanel } from './push/PushPanel'
import LoginPage from './pages/LoginPage'
import ProfileListPage from './pages/ProfileListPage'
import ProfileFormPage from './pages/ProfileFormPage'
import ExpertAssignmentsPage from './pages/ExpertAssignmentsPage'
import ExpertAssignmentPage from './pages/ExpertAssignmentPage'
import GarageDeclarationsPage from './pages/GarageDeclarationsPage'
import GarageDeclarationPage from './pages/GarageDeclarationPage'
import NewDeclarationPage from './pages/NewDeclarationPage'
import OfficerInboxPage from './pages/OfficerInboxPage'
import OfficerDeclarationPage from './pages/OfficerDeclarationPage'

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

/**
 * §5.2's garage view. `PushPanel` for the same reason `ExpertLayout` has it: §8 sends the garage a
 * popup when a declaration is approved or rejected, and a garage that never enabled notifications
 * should be offered them on whichever screen they are on.
 */
function GarageLayout() {
  if (!getTokens()) return <Navigate to="/login" replace />
  return (
    <main>
      <h1>AXA Motor Claims — Garage</h1>
      {/* §8's garage rows are about a decision on work this garage filed — never a claim assigned
          to them, which is what the default (expert) copy says. Found on screen in 4.2's pass. */}
      <PushPanel copy={GARAGE_PUSH_COPY} />
      <Outlet />
    </main>
  )
}

/**
 * §5.2's officer view. **No `PushPanel`** — §2 puts the officer on a desktop, and §8's officer row is
 * delivered by push *and* email, so the browser popup is a convenience here rather than the primary
 * trigger it is for a field user. Offering it on every screen would be noise.
 */
function OfficerLayout() {
  if (!getTokens()) return <Navigate to="/login" replace />
  return (
    <main>
      <h1>AXA Motor Claims — Claim officer</h1>
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
          {/*
            The paths are pinned by slice 4.1, not chosen here: `DeclarationService` pushes
            `/garage/{id}` and `/officer/{id}` as the notification's click target, and `sw.js` opens
            `data.url`. Renaming either route silently breaks §8's popups.
          */}
          <Route element={<GarageLayout />}>
            <Route path="/garage" element={<GarageDeclarationsPage />} />
            <Route path="/garage/new" element={<NewDeclarationPage />} />
            <Route path="/garage/:id" element={<GarageDeclarationPage />} />
          </Route>
          <Route element={<OfficerLayout />}>
            <Route path="/officer" element={<OfficerInboxPage />} />
            <Route path="/officer/:id" element={<OfficerDeclarationPage />} />
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
