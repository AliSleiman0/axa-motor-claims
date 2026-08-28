import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import ProfileFormPage from './ProfileFormPage'

const EXPERT = {
  id: '00000000-0000-0000-0000-0000000e0001',
  phone: '+999000003001',
  displayName: 'PLACEHOLDER Expert One',
  status: 'active',
  email: 'expert-one@example.invalid',
  next3Id: 'PLACEHOLDER-EXP-01',
}

/**
 * A1's create/edit form (design.md §5.4) — **its first test file, added in slice 7.2**.
 *
 * The gap and the defect coincided, which is the reason it is worth saying out loud: this was the
 * one page in `pages/` with no test, and it was also the one with an uncaught `fetch`. A failed load
 * produced an unhandled rejection and an *empty form* — every field blank, Save live, and one press
 * away from writing that emptiness over a real profile.
 */
describe('A1 — the profile form', () => {
  let calls: string[]

  beforeEach(() => {
    localStorage.clear()
    calls = []
  })

  it('loads the profile it is editing into the fields', async () => {
    stub({ getStatus: 200 })

    show(EXPERT.id)

    await waitFor(() => {
      expect((screen.getByLabelText(/Display name/i) as HTMLInputElement).value).toBe(
        EXPERT.displayName,
      )
    })

    // §4: the phone is the identity §9 signs in with, so it is locked after creation.
    expect((screen.getByLabelText(/Phone/i) as HTMLInputElement).disabled).toBe(true)
    expect(calls).toContain(`GET /api/admin/experts/${EXPERT.id}`)
  })

  /**
   * The failure that was silent. It is not only that nothing was said — the form rendered *empty*,
   * which is a form an administrator can submit, and a PUT of blank required fields over a live
   * profile is the shape of an outage that looks like a data-entry mistake weeks later.
   */
  it('says a failed load failed, and says nothing has been changed', async () => {
    stub({ getStatus: 503 })

    show(EXPERT.id)

    const banner = await screen.findByRole('alert')
    expect(banner.textContent).toContain('could not be loaded')
    expect(banner.textContent).toContain('503')
    expect(banner.textContent).toContain('Nothing has been changed')
  })

  it('still saves a new profile, and sends what was typed', async () => {
    const user = userEvent.setup()
    stub({ getStatus: 200 })

    show()

    await user.type(screen.getByLabelText(/Phone/i), '+999000003077')
    await user.type(screen.getByLabelText(/Display name/i), 'PLACEHOLDER Expert New')
    // Every required field, because the browser refuses the submit otherwise and the test would be
    // asserting HTML validation rather than the page.
    await user.type(screen.getByLabelText(/^Email/i), 'expert-new@example.invalid')
    await user.click(screen.getByRole('button', { name: /Create and send invite/i }))

    await waitFor(() => {
      expect(calls).toContain('POST /api/admin/experts')
    })
    expect(bodies[0]).toContain('PLACEHOLDER Expert New')
  })

  let bodies: string[] = []

  function stub({ getStatus }: { getStatus: number }) {
    bodies = []
    vi.stubGlobal(
      'fetch',
      vi.fn((url: string, init?: RequestInit) => {
        calls.push(`${init?.method ?? 'GET'} ${String(url)}`)
        if (init?.method === 'POST' || init?.method === 'PUT') {
          bodies.push(String(init.body ?? ''))
          return Promise.resolve(new Response('', { status: 200 }))
        }
        return Promise.resolve(
          new Response(JSON.stringify(EXPERT), { status: getStatus }),
        )
      }),
    )
  }
})

/** `id` omitted renders the create form, exactly as `App.tsx` routes it. */
function show(id?: string) {
  const path = id ? `/admin/experts/${id}` : '/admin/experts/new'

  render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/admin/:kind/new" element={<ProfileFormPage />} />
        <Route path="/admin/:kind/:id" element={<ProfileFormPage />} />
      </Routes>
    </MemoryRouter>,
  )
}
