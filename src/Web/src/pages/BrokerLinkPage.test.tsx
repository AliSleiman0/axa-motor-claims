import { fireEvent } from '@testing-library/dom'
import { act, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { TestQueryProvider } from '../testing/TestQueryProvider'
import BrokerLinkPage from './BrokerLinkPage'

const TYPES = ['MOTOR ALL RISK', 'MOTOR TOTAL LOSS', 'PLACEHOLDER-TYPE-3']
const TOKEN = 'kQ8sV2mJ4pR7wT1xY6zB3nC5dF9gH0jL'

let fetchMock: ReturnType<typeof vi.fn>

describe('B3 — the customer link', () => {
  beforeEach(() => {
    vi.stubGlobal('navigator', { ...navigator, clipboard: { writeText: () => Promise.resolve() } })
  })

  it('makes the mobile optional and lets the customer choose the type', async () => {
    show()

    // Both fields are optional (pass-3 decision 4): the mobile is only there so the broker knows
    // whose link this is, and the type preset is a convenience.
    await screen.findByRole('option', { name: TYPES[0] })
    expect(screen.getByRole('option', { name: 'Let the customer choose' })).toBeTruthy()

    const create = screen.getByRole('button', { name: 'Create link' })
    expect(create.hasAttribute('disabled')).toBe(false)
  })

  it('shows the link once, as the web route rather than the API path', async () => {
    show()

    await act(async () => {
      (await screen.findByRole('button', { name: 'Create link' })).click()
    })

    // The API answers `/public/{token}`; `/p/{token}` is the friendlier address in front of it, and
    // the origin is wherever this app is served from. A customer opening it is opening *this* app.
    const link = await screen.findByText(new RegExp(`/p/${TOKEN}$`))
    expect(link.textContent).not.toContain('/public/')
  })

  it('says the link cannot be shown again, and offers no way back to it', async () => {
    show()

    await act(async () => {
      (await screen.findByRole('button', { name: 'Create link' })).click()
    })

    await screen.findByRole('button', { name: 'Copy link' })
    expect(screen.getByText(/cannot be shown again/)).toBeTruthy()

    // §9.1 stores only the hash, so the form that created it is gone — leaving it would suggest the
    // value could be produced a second time.
    expect(screen.queryByRole('button', { name: 'Create link' })).toBeNull()
  })

  it('disables Send by SMS and names the reason', async () => {
    show()

    await act(async () => {
      (await screen.findByRole('button', { name: 'Create link' })).click()
    })

    const sms = await screen.findByRole('button', { name: 'Send by SMS' })
    expect(sms.hasAttribute('disabled')).toBe(true)

    // A disabled control with no explanation is a defect report waiting to happen; #24a is the
    // reason and `PublicLink.DeliveryChannel` is the switch.
    expect(screen.getByText(/delivery is set to copy/)).toBeTruthy()
  })

  it('creates one link for three clicks in the same tick', async () => {
    show()

    const create = await screen.findByRole('button', { name: 'Create link' })

    await act(async () => {
      create.click()
      create.click()
      create.click()
    })

    // A second link is a second request row and a second live credential in the broker's hands, for
    // one press — and only one of them is on screen.
    await waitFor(() => {
      expect(postsTo('/api/broker/link-requests')).toBe(1)
    })
  })

  it('explains a rejected mobile number in words', async () => {
    show({ failWith: 'invalid_phone' })

    const mobile = await screen.findByLabelText('Customer mobile')
    await act(async () => {
      fireEvent.change(mobile, { target: { value: '07700900000' } })
    })

    await act(async () => {
      screen.getByRole('button', { name: 'Create link' }).click()
    })

    expect(await screen.findByText(/international form/)).toBeTruthy()
  })
})

function postsTo(suffix: string): number {
  return fetchMock.mock.calls.filter(([url, init]) => {
    const request = init as RequestInit | undefined
    return String(url).endsWith(suffix) && request?.method === 'POST'
  }).length
}

function show({ failWith }: { failWith?: string } = {}) {
  fetchMock = vi.fn((url: string) => {
    const path = String(url)
    if (path.endsWith('/api/broker/config')) {
      return Promise.resolve(json({ insuranceTypes: TYPES }))
    }

    if (failWith) {
      return Promise.resolve(
        new Response(JSON.stringify({ error: failWith }), { status: 400 }),
      )
    }

    return Promise.resolve(
      json({
        requestId: '00000000-0000-0000-0000-0000000b0002',
        token: TOKEN,
        url: `/public/${TOKEN}`,
        expiresAt: '2026-08-29T11:12:00',
      }),
    )
  })
  vi.stubGlobal('fetch', fetchMock)

  render(
    <TestQueryProvider>
      <MemoryRouter initialEntries={['/broker/link']}>
        <Routes>
          <Route path="/broker/link" element={<BrokerLinkPage />} />
        </Routes>
      </MemoryRouter>
    </TestQueryProvider>,
  )
}

function json(body: unknown): Response {
  return new Response(JSON.stringify(body), { status: 200 })
}
