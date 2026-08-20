import { render, screen, within, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { AssignmentDetail } from '../expert/api'
import type { MediaConfig } from '../media/config'
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

const ARRIVED_AT = '2026-08-20T09:30:00'

/** Slice 2.5: E3's panels read §7.2's thresholds and §7.1's rules from the server. */
const MEDIA_CONFIG: MediaConfig = {
  clarity: { minWidth: 1024, minHeight: 768, blurVarianceThreshold: 100, blurAnalysisMaxEdge: 512 },
  maxFileMb: 15,
  buckets: [
    { bucket: 'insured_documents', allowUpload: true, contentTypes: ['image/jpeg'] },
    { bucket: 'insured_car_photo', allowUpload: false, contentTypes: ['image/jpeg'] },
    { bucket: 'tp_documents', allowUpload: true, contentTypes: ['image/jpeg'] },
    { bucket: 'tp_car_photo', allowUpload: false, contentTypes: ['image/jpeg'] },
    // Slice 3.2's E5. A report is a document, not a car photo (§5.1), so a PDF may be picked.
    {
      bucket: 'expert_report',
      allowUpload: true,
      contentTypes: ['image/jpeg', 'image/png', 'application/pdf'],
    },
    // Slice 3.1's two. Neither exists as a file to pick, so neither allows upload.
    { bucket: 'voice_note', allowUpload: false, contentTypes: ['audio/webm', 'audio/mp4'] },
    { bucket: 'damage_diagram', allowUpload: false, contentTypes: ['image/png'] },
  ],
}

/**
 * E2 issues three GETs since 2.5 — the claim, its documents, and the media config — so the stub has
 * to answer by route. Only the claim response varies per test; the others are constant.
 */
function routed(detail: () => Response = () => json(DETAIL)) {
  return (url: string): Promise<Response> => {
    const path = String(url)
    if (path.endsWith('/api/config/media')) return Promise.resolve(json(MEDIA_CONFIG))
    if (path.endsWith('/documents')) return Promise.resolve(json([]))
    if (path.endsWith('/arrival')) {
      return Promise.resolve(json({ arrivedAt: ARRIVED_AT, latitude: 25.2048, longitude: 55.2708 }))
    }
    return Promise.resolve(detail())
  }
}

describe('E2 — claim detail', () => {
  let fetchMock: ReturnType<typeof vi.fn>

  beforeEach(() => {
    fetchMock = vi.fn(routed())
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
    show()

    await userEvent.click(await screen.findByRole('button', { name: 'Arrived' }))

    await waitFor(() =>
      expect(screen.getByRole('button', { name: 'Arrived' }).hasAttribute('disabled')).toBe(true),
    )
    expect(arrivalRequests(fetchMock)).toHaveLength(1)
  })

  it('arrives disabled when the assignment already has an arrival', async () => {
    allowLocation()
    fetchMock.mockImplementation(routed(() => json({ ...DETAIL, arrivedAt: ARRIVED_AT })))
    show()

    const button = await screen.findByRole('button', { name: 'Arrived' })
    expect(button.hasAttribute('disabled')).toBe(true)
  })

  it('shows the staleness banner with the age of the data', async () => {
    // §4: "if NEXT3 is down, serve stale with a staleness banner".
    fetchMock.mockImplementation(routed(() => json({ ...DETAIL, claimStatus: 'stale' })))
    show()

    expect(await screen.findByText(/NEXT3 is unreachable/)).toBeDefined()
  })

  it('returns the expert to the list they searched', async () => {
    // The other half of E1 carrying its search into the claim link. `show()` mounts at a path with
    // no query, so the plain case is covered by the assertion below being reached at all.
    showAt(`/expert/${ASSIGNMENT_ID}?q=PLC-TEST-T2`)

    const back = await screen.findByRole('link', { name: '← My claims' })
    expect(back.getAttribute('href')).toBe('/expert?q=PLC-TEST-T2')
  })

  it('offers all five capture buckets before the expert has arrived', async () => {
    // §5.1's recorded interpretation, on the screen: Arrived is NOT a precondition for capture. The
    // diagram implies an order, the BRD never states the gate, and an expert whose GPS is slow must
    // still be able to photograph the car. DETAIL has `arrivedAt: null`, so this is the ungated case.
    show()

    expect(await screen.findByText('Insured documents')).toBeDefined()
    expect(screen.getByText('Insured car photos')).toBeDefined()
    expect(screen.getByText('Third-party documents')).toBeDefined()
    expect(screen.getByText('Third-party car photos')).toBeDefined()
    // E5 (slice 3.2). It is a panel on this screen, not a route of its own — §5.1 gives the report
    // no screen, so the smaller interpretation is the one that ships.
    expect(screen.getByText('Expert report')).toBeDefined()
    expect(screen.getByRole('button', { name: 'Arrived' }).hasAttribute('disabled')).toBe(false)
  })

  it('lets the expert pick a report file (E5)', async () => {
    // The whole of E5's web side is one entry in EXPERT_BUCKETS, so what is worth pinning is that
    // the entry actually produces a usable panel rather than 2.5's "not configured" alert — which
    // is what a bucket missing from the server's registry renders, silently and for ever.
    //
    // Scoped to this panel by its heading, because every panel labels its inputs identically: five
    // controls on this screen say "Take a photo" and three say "Choose a file". The counting tests
    // below live with that; a test about one bucket cannot.
    show()

    // Awaited on a control rather than on the heading — 3.1's lesson. The heading renders while the
    // media config is still in flight, so a heading proves the panel exists, not that it is usable.
    await screen.findAllByLabelText(/^Choose a file/)

    const heading = screen.getByRole('heading', { name: /^Expert report/ })
    const panel = within(heading.closest('section')!)

    expect(panel.queryByRole('alert')).toBeNull()
    // §5.1: "upload allowed — a report is a document, not a car photo", and §7.1 gives it the
    // document allow-list, so the PDF an expert writes their report in is pickable.
    expect(panel.getByLabelText('Choose a file').getAttribute('accept')).toContain('application/pdf')
  })

  it('offers no gallery picker for the car-photo buckets', async () => {
    // §7.1's capture-only rule, reaching the screen the expert actually uses. Three buckets allow
    // a file and two do not, so the count is what discriminates. It moved from 4/2 to 5/3 in slice
    // 3.2 because E5 added a bucket that allows both — the rule is unchanged, only the arithmetic.
    show()

    expect(await screen.findAllByLabelText(/^Take a photo/)).toHaveLength(5)
    expect(screen.getAllByLabelText(/^Choose a file/)).toHaveLength(3)
  })

  it('offers the voice note and the damage diagram, also ungated on arrival', async () => {
    // Slice 3.1's two artifacts sit under the same section as the four buckets and take no arrival
    // prop either — §5.1's ungated-capture interpretation is structural, not a condition to flip.
    show()

    // Awaited on the control, not the heading: the heading renders while the media config is still
    // in flight, so finding it proves only that the panel exists, not that it became usable.
    expect(await screen.findByRole('button', { name: 'Record a voice note' })).toBeDefined()

    // Matched loosely because both headings carry a media count once the document list lands —
    // "Voice note (0)". An exact match here passes or fails on which query resolved first.
    expect(screen.getByRole('heading', { name: /^Voice note/ })).toBeDefined()
    expect(screen.getByRole('heading', { name: /^Damage diagram/ })).toBeDefined()
    expect(screen.getByRole('button', { name: 'Use this diagram' })).toBeDefined()
    expect(screen.getByRole('checkbox', { name: 'Bonnet' })).toBeDefined()

    expect(screen.getByRole('button', { name: 'Arrived' }).hasAttribute('disabled')).toBe(false)
  })

  it('offers no file picker for either in-app artifact', async () => {
    // Both are produced by the app, so the count of pickers must not move: still the two document
    // buckets plus E5's report, and nothing else.
    show()

    expect(await screen.findAllByLabelText(/^Take a photo/)).toHaveLength(5)
    expect(screen.getAllByLabelText(/^Choose a file/)).toHaveLength(3)
  })

  it('calls an outage an outage rather than a missing claim', async () => {
    fetchMock.mockImplementation(
      routed(() => new Response('{"error":"next3_unavailable"}', { status: 503 })),
    )
    show()

    expect(await screen.findByText(/NEXT3 is unreachable and this claim has never been loaded/))
      .toBeDefined()
  })
})

function show() {
  showAt(`/expert/${ASSIGNMENT_ID}`)
}

/** Same render, at an explicit entry — E2 now reads the query string off it (slice 3.2). */
function showAt(entry: string) {
  render(
    <TestQueryProvider>
      <MemoryRouter initialEntries={[entry]}>
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
