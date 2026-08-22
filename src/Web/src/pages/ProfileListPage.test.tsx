import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import ProfileListPage from './ProfileListPage'

const ACTIVE = {
  id: '00000000-0000-0000-0000-0000000e0001',
  phone: '+999000003001',
  displayName: 'PLACEHOLDER Expert One',
  status: 'active',
  email: 'expert-one@example.invalid',
  next3Id: 'PLACEHOLDER-EXP-01',
}

const INVITED = {
  id: '00000000-0000-0000-0000-0000000e0002',
  phone: '+999000009001',
  displayName: 'PLACEHOLDER Expert Two',
  status: 'invited',
  email: 'expert-two@example.invalid',
  next3Id: null,
}

/** Kept for the record, cannot sign in. No reactivate and no delete anywhere in this flow. */
const INACTIVE = {
  id: '00000000-0000-0000-0000-0000000e0003',
  phone: '+999000003009',
  displayName: 'PLACEHOLDER Expert Three',
  status: 'inactive',
  email: 'expert-three@example.invalid',
  next3Id: 'PLACEHOLDER-EXP-03',
}

describe('A1 — the profile list', () => {
  let calls: string[]

  beforeEach(() => {
    localStorage.clear()
    calls = []
    vi.stubGlobal(
      'fetch',
      vi.fn((url: string, init?: RequestInit) => {
        calls.push(`${init?.method ?? 'GET'} ${String(url)}`)
        if (init?.method === 'POST') return Promise.resolve(new Response('', { status: 200 }))
        return Promise.resolve(
          new Response(JSON.stringify([ACTIVE, INVITED, INACTIVE]), { status: 200 }),
        )
      }),
    )
  })

  it('gives each status its own row treatment', async () => {
    // Three states, three treatments (pass 3). The chip names the status in words; the muted row is
    // for the admin scanning a long list, who should see who is switched off without reading a
    // column. Colour is never the only signal — the chip is still there.
    show()

    const inactive = (await screen.findByText(INACTIVE.displayName)).closest('tr')!
    const active = screen.getByText(ACTIVE.displayName).closest('tr')!

    expect(inactive.className).toContain('worklist__row--muted')
    expect(active.className).not.toContain('worklist__row--muted')
    expect(within(inactive).getByText('Inactive')).toBeDefined()
    expect(within(active).getByText('Active')).toBeDefined()
  })

  it('offers Re-send invite only while a profile is still invited', async () => {
    // Once somebody has signed in there is nothing to re-send, and the server refuses it anyway.
    show()

    await screen.findByText(INVITED.displayName)
    const invited = screen.getByText(INVITED.displayName).closest('tr')!
    const active = screen.getByText(ACTIVE.displayName).closest('tr')!

    expect(within(invited).getByRole('button', { name: 'Re-send invite' })).toBeDefined()
    expect(within(active).queryByRole('button', { name: 'Re-send invite' })).toBeNull()
  })

  it('offers no Deactivate on a profile that is already inactive', async () => {
    show()

    const inactive = (await screen.findByText(INACTIVE.displayName)).closest('tr')!
    expect(within(inactive).queryByRole('button', { name: 'Deactivate' })).toBeNull()
  })

  describe('deactivating', () => {
    it('asks before it acts, and names who', async () => {
      // The one destructive action on the screen, and the only one that asks. It used to be a
      // `window.confirm`, which could only be tested by stubbing a global and asserting the stub.
      const user = userEvent.setup()
      show()

      await user.click(await findDeactivate(ACTIVE.displayName))

      expect(
        screen.getByText(
          `Deactivate ${ACTIVE.displayName}? They will no longer be able to sign in.`,
        ),
      ).toBeDefined()
      // Nothing has happened yet — asking is not doing.
      expect(calls.some((call) => call.startsWith('POST'))).toBe(false)
    })

    it('does nothing on Cancel', async () => {
      const user = userEvent.setup()
      show()

      await user.click(await findDeactivate(ACTIVE.displayName))
      await user.click(screen.getByRole('button', { name: 'Cancel' }))

      expect(screen.queryByText(/will no longer be able to sign in/)).toBeNull()
      expect(calls.some((call) => call.startsWith('POST'))).toBe(false)
    })

    it('posts to the right user on Deactivate, and reloads the list', async () => {
      const user = userEvent.setup()
      show()

      await user.click(await findDeactivate(ACTIVE.displayName))
      // The dialog's own button, not the row's — both say "Deactivate", which is correct: a button
      // names what it does, and the dialog is the one that does it.
      await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Deactivate' }))

      await waitFor(() => {
        expect(calls).toContain(`POST /api/admin/users/${ACTIVE.id}/deactivate`)
      })
      // A second GET: the row's status has changed and the screen must not go on showing the old one.
      await waitFor(() => {
        expect(calls.filter((call) => call.startsWith('GET')).length).toBeGreaterThan(1)
      })
    })
  })

  it('says "Invite sent." on the page rather than in an alert', async () => {
    const user = userEvent.setup()
    show()

    await screen.findByText(INVITED.displayName)
    const invited = screen.getByText(INVITED.displayName).closest('tr')!
    await user.click(within(invited).getByRole('button', { name: 'Re-send invite' }))

    expect(await screen.findByText('Invite sent.')).toBeDefined()
    expect(calls).toContain(`POST /api/admin/users/${INVITED.id}/invite`)
  })
})

async function findDeactivate(displayName: string) {
  const row = (await screen.findByText(displayName)).closest('tr')!
  return within(row).getByRole('button', { name: 'Deactivate' })
}

function show() {
  render(
    <MemoryRouter initialEntries={['/admin/experts']}>
      <Routes>
        <Route path="/admin/:kind" element={<ProfileListPage />} />
      </Routes>
    </MemoryRouter>,
  )
}
