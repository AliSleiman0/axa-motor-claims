import { QueryClientProvider } from '@tanstack/react-query'
import { BrowserRouter, Navigate, Outlet, Route, Routes } from 'react-router-dom'
import { createQueryClient } from './api/queryClient'
import { currentRole, homePathFor } from './api/session'
import { getTokens } from './api/tokens'
import { useOutboxFailedCount } from './admin/outbox'
import { GARAGE_PUSH_COPY } from './push/copy'
import { PushPanel } from './push/PushPanel'
import { AppShell } from './ui/AppShell'
import LoginPage from './pages/LoginPage'
import InvitePage from './pages/InvitePage'
import PublicRequestPage from './public/PublicRequestPage'
import AdminFailedPushesPage from './pages/AdminFailedPushesPage'
import ProfileListPage from './pages/ProfileListPage'
import ProfileFormPage from './pages/ProfileFormPage'
import ExpertAssignmentsPage from './pages/ExpertAssignmentsPage'
import ExpertAssignmentPage from './pages/ExpertAssignmentPage'
import GarageDeclarationsPage from './pages/GarageDeclarationsPage'
import GarageDeclarationPage from './pages/GarageDeclarationPage'
import NewDeclarationPage from './pages/NewDeclarationPage'
import OfficerInboxPage from './pages/OfficerInboxPage'
import OfficerDeclarationPage from './pages/OfficerDeclarationPage'
import BrokerRequestsPage from './pages/BrokerRequestsPage'
import BrokerRequestPage from './pages/BrokerRequestPage'
import NewBrokerRequestPage from './pages/NewBrokerRequestPage'
import BrokerLinkPage from './pages/BrokerLinkPage'

const queryClient = createQueryClient()

/**
 * The four layouts are now one shell plus whatever that role needs on every screen.
 *
 * `AppShell` takes the role rather than reading it, so the difference between an expert's 48 px
 * controls and an officer's 36 px is one string and not four copies of a layout.
 */
function AdminLayout() {
  // Called here rather than inside `ui/`, which may not import a role module — so the one number in
  // the chrome is fetched by the shell that owns it and passed down (slice 6.2). It runs on all five
  // admin screens on purpose: nobody opens A2 on a hunch, so a failure that never announces itself is
  // never read.
  const failed = useOutboxFailedCount()

  if (!getTokens()) return <Navigate to="/login" replace />

  return (
    <AppShell role="admin" tabCounts={{ '/admin/failed-pushes': failed.data?.failed ?? 0 }}>
      <Outlet />
    </AppShell>
  )
}

function ExpertLayout() {
  if (!getTokens()) return <Navigate to="/login" replace />
  return (
    <AppShell role="expert">
      {/* In the layout, not on a page: §8's assignment popup is the BRD's primary trigger, and an
          expert who has not enabled it should be offered it on every screen, not only the one they
          happened to land on. */}
      <PushPanel />
      <Outlet />
    </AppShell>
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
    <AppShell role="garage">
      {/* §8's garage rows are about a decision on work this garage filed — never a claim assigned
          to them, which is what the default (expert) copy says. Found on screen in 4.2's pass. */}
      <PushPanel copy={GARAGE_PUSH_COPY} />
      <Outlet />
    </AppShell>
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
    <AppShell role="claim_officer">
      <Outlet />
    </AppShell>
  )
}

/**
 * §5.3's broker view (slice 5.2). **No `PushPanel`**, on `OfficerLayout`'s reasoning: §2 puts the
 * broker at a desk, and the one push §8 sends them — "Option 2 file ready to send" — belongs to 5.3,
 * which pairs it with an email fallback. Offering the prompt on every screen before anything can
 * arrive would be noise.
 */
function BrokerLayout() {
  if (!getTokens()) return <Navigate to="/login" replace />
  return (
    <AppShell role="broker">
      <Outlet />
    </AppShell>
  )
}

function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          {/*
            S1, and the only other unauthenticated route in the app. Two spellings of one screen: the
            link in the invitation SMS carries the token in the path, and the bare route is the paste
            path for a message whose URL was stripped or read on another device.

            `/invite/:token` is a contract with `InviteService.InviteMessage` in the same way
            `/garage/:id` is one with §8's push URLs — renaming it silently breaks every invitation
            already sent, which is up to seven days' worth.
          */}
          <Route path="/invite" element={<InvitePage />} />
          <Route path="/invite/:token" element={<InvitePage />} />
          {/*
            P1 (§5.3), and the **third** unauthenticated route — deliberately outside every layout
            element, so no token guard runs and no `AppShell` is mounted. `AppHeader` calls `useMe`
            and `useSignOut`; a member of the public has neither, and a sign-out control on a page
            with no account is nonsense before it is a bug.

            `/p/:token` is a contract with `BrokerLinkEndpoints` in the way `/invite/:token` is one
            with `InviteService`: B3 renders `{Auth:AppBaseUrl}/p/{token}` and a broker hands that
            string to a customer, so renaming it breaks every link already issued — up to
            `PublicLink:ValidityDays` worth. Short on purpose: it is typed off a phone screen.
          */}
          <Route path="/p/:token" element={<PublicRequestPage />} />
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
          {/* `/broker/{id}` is 5.3's push target as well as B1's Review link, so it is pinned the
              way the garage and officer paths are. `new` and `link` precede `:id` — a literal
              segment must not be eaten by the parameter route. */}
          <Route element={<BrokerLayout />}>
            <Route path="/broker" element={<BrokerRequestsPage />} />
            <Route path="/broker/new" element={<NewBrokerRequestPage />} />
            <Route path="/broker/link" element={<BrokerLinkPage />} />
            <Route path="/broker/:id" element={<BrokerRequestPage />} />
          </Route>
          <Route element={<AdminLayout />}>
            {/*
              Before `/admin/:kind`, or the parameter route swallows it — today it does, and the
              screen renders A1's "Unknown profile type." The broker block's `new`/`link` sit above
              `:id` for the same reason.
            */}
            <Route path="/admin/failed-pushes" element={<AdminFailedPushesPage />} />
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
