import { BrowserRouter, Link, Navigate, Outlet, Route, Routes } from 'react-router-dom'
import { getTokens } from './api/tokens'
import { PROFILE_KINDS } from './admin/kinds'
import LoginPage from './pages/LoginPage'
import ProfileListPage from './pages/ProfileListPage'
import ProfileFormPage from './pages/ProfileFormPage'

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

function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route element={<AdminLayout />}>
          <Route path="/" element={<Navigate to="/admin/experts" replace />} />
          <Route path="/admin/:kind" element={<ProfileListPage />} />
          <Route path="/admin/:kind/new" element={<ProfileFormPage />} />
          <Route path="/admin/:kind/:id" element={<ProfileFormPage />} />
        </Route>
      </Routes>
    </BrowserRouter>
  )
}

export default App
