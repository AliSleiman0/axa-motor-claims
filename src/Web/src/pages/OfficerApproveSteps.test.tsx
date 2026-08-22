import { render, screen, waitFor } from '@testing-library/react'
import { fireEvent } from '@testing-library/dom'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { OfficerDeclarationDetail } from '../officer/api'
import { TestQueryProvider } from '../testing/TestQueryProvider'
import OfficerDeclarationPage from './OfficerDeclarationPage'

const ID = '00000000-0000-0000-0000-0000000d0001'
const VISA = 'PLACEHOLDER-VISA-0001'

const DETAIL: OfficerDeclarationDetail = {
  id: ID,
  state: 'submitted',
  plateNo: 'PLC-TEST-D1',
  insuredName: 'PLACEHOLDER Insured',
  note: null,
  visaNo: null,
  createdAt: '2026-08-22T08:00:00',
  submittedAt: '2026-08-22T08:30:00',
  decidedAt: null,
  garageName: 'PLACEHOLDER Garage Contact',
  garageEmail: 'PLACEHOLDER-garage@example.invalid',
  garagePhone: '+999000008001',
  comments: [],
}

const ME = {
  id: 'u1',
  phone: '+999000000009',
  role: 'claim_officer',
  displayName: 'PLACEHOLDER Officer',
}

/**
 * O2's approve sequence, named on screen (pass 3's `O2States`).
 *
 * **Why this is worth a test of its own.** §5.2's approval is three real steps in a fixed order —
 * the browser draws #18's decision image, uploads it, and only then commits — and the steps are not
 * equivalent: a failure at one or two leaves the declaration undecided, while the third is the one
 * irreversible act in the whole flow. A single flat "Working…" tells the officer none of that, and
 * the ordering itself has no other observable symptom from outside, which is exactly the shape of
 * thing that silently reverses during a later refactor.
 *
 * Each step is held open by a promise this test resolves, so the label is read *while* that step is
 * running rather than inferred from the order of the calls afterwards.
 */
describe('O2 — the approve sequence is named on screen', () => {
  let release: { render?: () => void; upload?: () => void; approve?: () => void }

  beforeEach(() => {
    release = {}
    vi.stubGlobal('URL', {
      ...URL,
      createObjectURL: () => 'blob:PLACEHOLDER',
      revokeObjectURL: vi.fn(),
    })
  })

  it('says Rendering decision…, then Uploading…, then Approving…', async () => {
    show()

    fireEvent.change(await screen.findByLabelText('Visa number'), { target: { value: VISA } })
    fireEvent.click(screen.getByRole('button', { name: 'Approve' }))

    // Step one. Nothing has left the browser yet — the image does not exist.
    expect(await screen.findByRole('button', { name: 'Rendering decision…' })).toBeDefined()
    // One latch across both buttons: they are alternatives, so a second press cannot start a
    // second decision. Asserted here rather than separately because it holds for every step.
    expect(screen.getByRole('button', { name: 'Reject' }).hasAttribute('disabled')).toBe(true)

    release.render?.()
    expect(await screen.findByRole('button', { name: 'Uploading…' })).toBeDefined()

    release.upload?.()
    expect(await screen.findByRole('button', { name: 'Approving…' })).toBeDefined()

    release.approve?.()
    // Back to a decided declaration, which has no controls at all.
    await waitFor(() => {
      expect(screen.queryByRole('button', { name: /Approving|Uploading|Rendering/ })).toBeNull()
    })
  })

  it('leaves a rejection saying Working…, because it is one call', async () => {
    // Nothing to narrate: a rejection touches nothing outside this app, renders no image and
    // uploads nothing. Naming steps it does not have would be theatre.
    show()

    fireEvent.click(await screen.findByRole('button', { name: 'Reject' }))

    expect(await screen.findByRole('button', { name: 'Working…' })).toBeDefined()
    release.approve?.()
  })

  it('stops claiming a step when one fails', async () => {
    // `onSettled` clears the step on failure as well as success. A button still reading "Uploading…"
    // after the upload was refused would be the screen contradicting the error beneath it — and this
    // is the failure that matters most, because the declaration is still undecided.
    show({ uploadStatus: 413 })

    fireEvent.change(await screen.findByLabelText('Visa number'), { target: { value: VISA } })
    fireEvent.click(screen.getByRole('button', { name: 'Approve' }))

    // Each gate is created inside the step it guards, so a step has to be *observed* before it can
    // be released — releasing blind resolves nothing and the test hangs on the label it just left.
    await screen.findByRole('button', { name: 'Rendering decision…' })
    release.render?.()
    await screen.findByRole('button', { name: 'Uploading…' })
    release.upload?.()

    expect(await screen.findByRole('button', { name: 'Approve' })).toBeDefined()
    expect(screen.getByRole('alert').textContent).toBeTruthy()
  })

  function show({ uploadStatus = 201 }: { uploadStatus?: number } = {}) {
    const gate = (key: keyof typeof release) =>
      new Promise<void>((resolve) => {
        release[key] = resolve
      })

    const renderPng = async () => {
      await gate('render')
      return new Blob([new Uint8Array([1, 2, 3])])
    }

    vi.stubGlobal(
      'fetch',
      vi.fn(async (url: string, init?: RequestInit) => {
        const path = String(url)
        const method = init?.method ?? 'GET'

        if (path.endsWith('/auth/me')) return json(ME)
        if (path.endsWith('/documents') && method === 'POST') {
          await gate('upload')
          return uploadStatus === 201
            ? json({ id: 'doc-approval' }, 201)
            : new Response(JSON.stringify({ error: 'file_too_large' }), { status: uploadStatus })
        }
        if (path.endsWith('/documents')) return json([])
        if (path.endsWith('/approve') || path.endsWith('/reject')) {
          await gate('approve')
          return json({ state: 'approved' })
        }
        return json(DETAIL)
      }),
    )

    render(
      <TestQueryProvider>
        <MemoryRouter initialEntries={[`/officer/${ID}`]}>
          <Routes>
            <Route path="/officer/:id" element={<OfficerDeclarationPage render={renderPng} />} />
          </Routes>
        </MemoryRouter>
      </TestQueryProvider>,
    )
  }
})

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status })
}
