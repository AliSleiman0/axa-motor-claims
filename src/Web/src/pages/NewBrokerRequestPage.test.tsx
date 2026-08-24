import { fireEvent } from '@testing-library/dom'
import { act, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'
import { TestQueryProvider } from '../testing/TestQueryProvider'
import NewBrokerRequestPage from './NewBrokerRequestPage'

const TYPES = ['MOTOR ALL RISK', 'MOTOR TOTAL LOSS', 'PLACEHOLDER-TYPE-3']

let fetchMock: ReturnType<typeof vi.fn>

describe('B2 — the six-field form', () => {
  it('offers the insurance types the server sends, and none of its own', async () => {
    show()

    // Awaited on an *option*, not on the select: the control renders before `/api/broker/config`
    // has landed, so awaiting the label would prove the field exists rather than that it is usable —
    // the same way G3's capture test first failed.
    await screen.findByRole('option', { name: TYPES[0] })
    const select = screen.getByLabelText('Insurance type')
    const options = [...select.querySelectorAll('option')].map((o) => o.textContent)

    // The list is `Broker:InsuranceTypes` (#14) over the wire. A literal in the component would be
    // client data in TypeScript — a placeholder-rule violation — and a second source of truth from
    // the one the server validates the submission against.
    expect(options).toEqual(['Choose a type', ...TYPES])
  })

  it('keeps Submit disabled until all six are filled, and only then enables it', async () => {
    show()

    const save = await screen.findByRole('button', { name: 'Save details' })
    expect(save.hasAttribute('disabled')).toBe(true)

    await fill({ omit: 'effective-date' })
    expect(save.hasAttribute('disabled')).toBe(true)

    await set('effective-date', '2026-09-01')
    await waitFor(() => {
      expect(save.hasAttribute('disabled')).toBe(false)
    })
  })

  it('treats a zero amount as unfilled rather than as a value', async () => {
    show()

    const save = await screen.findByRole('button', { name: 'Save details' })
    await fill()
    await waitFor(() => {
      expect(save.hasAttribute('disabled')).toBe(false)
    })

    // Zero is what a tabbed-through number field holds, and the server refuses it
    // (`400 invalid_amount`). The button should not be offering to send it.
    await set('car-value', '0')
    await waitFor(() => {
      expect(save.hasAttribute('disabled')).toBe(true)
    })
  })

  it('shows the amounts with no currency symbol', async () => {
    show()

    // #47: the BRD names both amounts and no currency, so the field shows the number. A symbol here
    // would be an invented client literal, and the wrong one is worse than none.
    const carValue = (await screen.findByLabelText('Car value')) as HTMLInputElement
    expect(carValue.type).toBe('number')
    expect(carValue.closest('.field')!.textContent).not.toMatch(/[$£€]|AED|USD/)
  })

  it('posts once for three clicks in the same tick', async () => {
    show()

    const save = await screen.findByRole('button', { name: 'Save details' })
    await fill()
    await waitFor(() => {
      expect(save.hasAttribute('disabled')).toBe(false)
    })

    await act(async () => {
      save.click()
      save.click()
      save.click()
    })

    await waitFor(() => {
      expect(postsTo('/api/broker/requests')).toBe(1)
    })
  })
})

async function fill({ omit }: { omit?: string } = {}) {
  const values: [string, string][] = [
    ['insured-name', 'PLACEHOLDER Insured Three'],
    ['insurance-type', TYPES[0]],
    ['insured-address', 'PLACEHOLDER Address 1'],
    ['car-value', '25000'],
    ['estimated-premium', '1200'],
    ['effective-date', '2026-09-01'],
  ]

  for (const [id, value] of values) {
    if (id === omit) continue
    await set(id, value)
  }
}

async function set(id: string, value: string) {
  // `fireEvent` is fine for *filling* — it is only the latch assertion that needs raw, unflushed
  // clicks, and there the flush is exactly what would hide the bug.
  const control = document.getElementById(id)!
  await act(async () => {
    fireEvent.change(control, { target: { value } })
  })
}

function postsTo(suffix: string): number {
  return fetchMock.mock.calls.filter(([url, init]) => {
    const request = init as RequestInit | undefined
    return String(url).endsWith(suffix) && request?.method === 'POST'
  }).length
}

function show() {
  fetchMock = vi.fn((url: string) => {
    const path = String(url)
    if (path.endsWith('/api/broker/config')) {
      return Promise.resolve(json({ insuranceTypes: TYPES }))
    }
    return Promise.resolve(json({ id: '00000000-0000-0000-0000-0000000b0001' }))
  })
  vi.stubGlobal('fetch', fetchMock)

  render(
    <TestQueryProvider>
      <MemoryRouter initialEntries={['/broker/new']}>
        <Routes>
          <Route path="/broker/new" element={<NewBrokerRequestPage />} />
          <Route path="/broker/:id" element={<p>Request</p>} />
        </Routes>
      </MemoryRouter>
    </TestQueryProvider>,
  )
}

function json(body: unknown): Response {
  return new Response(JSON.stringify(body), { status: 200 })
}
