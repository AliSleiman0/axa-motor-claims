import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { AssignmentDetail } from '../expert/api'
import { TestQueryProvider } from '../testing/TestQueryProvider'
import ExpertAssignmentPage from './ExpertAssignmentPage'

const ASSIGNMENT_ID = '00000000-0000-0000-0000-00000000a11c'

const DETAIL: AssignmentDetail = {
  id: ASSIGNMENT_ID,
  visaNo: 'PLACEHOLDER-VISA-T00001',
  receivedAt: '2026-08-20T08:00:00',
  openedAt: '2026-08-20T08:05:00',
  arrivedAt: null,
  claimStatus: 'fresh',
  claimFetchedAt: '2026-08-20T08:05:00',
  claim: {
    visaNo: 'PLACEHOLDER-VISA-T00001',
    policyNo: 'PLACEHOLDER-POL-T01',
    plateNo: 'PLC-TEST-T1',
    insuredName: 'PLACEHOLDER Insured T',
    insuredPhone: '+999000009001',
    carMakeModel: 'PLACEHOLDER Make T',
    city: 'PLACEHOLDER City T',
    accidentDate: '2026-08-12',
  },
}

describe('E2 — claim detail', () => {
  let fetchMock: ReturnType<typeof vi.fn>

  beforeEach(() => {
    fetchMock = vi.fn(() => Promise.resolve(json(DETAIL)))
    vi.stubGlobal('fetch', fetchMock)
  })

  afterEach(() => {
    Reflect.deleteProperty(navigator, 'geolocation')
  })

  it('renders the claim NEXT3 returned', async () => {
    show()

    expect(await screen.findByText('PLACEHOLDER Insured T')).toBeDefined()
    expect(screen.getByText('PLC-TEST-T1')).toBeDefined()
  })

  it('explains the denial and sends nothing when location is refused', async () => {
    // §5.1: "geolocation-denied shows a blocking explanation (arrival without location is not sent)".
    denyLocation()
    show()
    const button = await screen.findByRole('button', { name: 'Arrived' })

    await userEvent.click(button)

    const alert = await screen.findByText(/Allow location access/)
    expect(alert).toBeDefined()
    expect(arrivalRequests(fetchMock)).toHaveLength(0)
    // Still pressable: fixing the permission and trying again is the whole point of the message.
    expect(screen.getByRole('button', { name: 'Arrived' }).hasAttribute('disabled')).toBe(false)
  })

  it('disables the button after a successful press', async () => {
    // §5.1: "Button disabled after first press."
    allowLocation()
    fetchMock.mockImplementation((url: string) =>
      Promise.resolve(
        url.endsWith('/arrival')
          ? json({ arrivedAt: '2026-08-20T09:30:00', latitude: 25.2048, longitude: 55.2708 })
          : json(DETAIL),
      ),
    )
    show()

    await userEvent.click(await screen.findByRole('button', { name: 'Arrived' }))

    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Arrived' }).hasAttribute('disabled')).toBe(true),
    )
    expect(arrivalRequests(fetchMock)).toHaveLength(1)
  })

  it('arrives disabled when the assignment already has an arrival', async () => {
    allowLocation()
    fetchMock.mockResolvedValue(json({ ...DETAIL, arrivedAt: '2026-08-20T09:30:00' }))
    show()

    const button = await screen.findByRole('button', { name: 'Arrived' })
    expect(button.hasAttribute('disabled')).toBe(true)
  })

  it('shows the staleness banner with the age of the data', async () => {
    // §4: "if NEXT3 is down, serve stale with a staleness banner".
    fetchMock.mockResolvedValue(json({ ...DETAIL, claimStatus: 'stale' }))
    show()

    expect(await screen.findByText(/NEXT3 is unreachable/)).toBeDefined()
  })

  it('calls an outage an outage rather than a missing claim', async () => {
    fetchMock.mockResolvedValue(new Response('{"error":"next3_unavailable"}', { status: 503 }))
    show()

    expect(await screen.findByText(/NEXT3 is unreachable and this claim has never been loaded/))
      .toBeDefined()
  })
})

function show() {
  render(
    <TestQueryProvider>
      <MemoryRouter initialEntries={[`/expert/${ASSIGNMENT_ID}`]}>
        <Routes>
          <Route path="/expert/:id" element={<ExpertAssignmentPage />} />
        </Routes>
      </MemoryRouter>
    </TestQueryProvider>,
  )
}

function json(body: unknown): Response {
  return new Response(JSON.stringify(body), { status: 200 })
}

function arrivalRequests(fetchMock: ReturnType<typeof vi.fn>): unknown[] {
  return fetchMock.mock.calls.filter(([url]) => String(url).endsWith('/arrival'))
}

function allowLocation() {
  defineGeolocation((success) =>
    success({ coords: { latitude: 25.2048, longitude: 55.2708 } } as GeolocationPosition),
  )
}

function denyLocation() {
  defineGeolocation((_success, failure) => failure?.({ code: 1 } as GeolocationPositionError))
}

function defineGeolocation(
  getCurrentPosition: (success: PositionCallback, failure?: PositionErrorCallback | null) => void,
) {
  Object.defineProperty(navigator, 'geolocation', {
    configurable: true,
    value: { getCurrentPosition: vi.fn(getCurrentPosition), watchPosition: vi.fn(), clearWatch: vi.fn() },
  })
}
