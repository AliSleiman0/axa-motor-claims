import { act, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { DeclarationDetail, DeclarationDocument, DeclarationState } from '../garage/api'
import type { MediaConfig } from '../media/config'
import { TestQueryProvider } from '../testing/TestQueryProvider'
import GarageDeclarationPage from './GarageDeclarationPage'

const ID = '00000000-0000-0000-0000-0000000d0001'

/** §7.1's two garage buckets as `GET /api/config/media` serves them (slice 4.1 added them). */
const MEDIA_CONFIG: MediaConfig = {
  clarity: { minWidth: 1024, minHeight: 768, blurVarianceThreshold: 100, blurAnalysisMaxEdge: 512 },
  maxFileMb: 15,
  buckets: [
    { bucket: 'garage_documents', allowUpload: true, contentTypes: ['image/jpeg', 'application/pdf'] },
    // Capture-only, the BRD's hard rule — so the panel must render no file picker at all.
    { bucket: 'garage_car_photo', allowUpload: false, contentTypes: ['image/jpeg'] },
  ],
}

const DOCUMENT: DeclarationDocument = {
  id: '00000000-0000-0000-0000-0000000dd001',
  bucket: 'garage_car_photo',
  docType: 'PLACEHOLDER-DOC-13',
  origin: 'captured',
  clarityResult: 'passed',
  contentType: 'image/jpeg',
  fileName: 'PLACEHOLDER-photo.jpg',
  sizeBytes: 1024,
  pushStatus: 'deferred',
  blobRetained: true,
  createdAt: '2026-08-22T09:00:00',
}

function detail(overrides: Partial<DeclarationDetail> = {}): DeclarationDetail {
  return {
    id: ID,
    state: 'draft',
    plateNo: 'PLC-TEST-D1',
    insuredName: 'PLACEHOLDER Insured',
    note: null,
    visaNo: null,
    createdAt: '2026-08-22T08:00:00',
    submittedAt: null,
    decidedAt: null,
    repairsStartedAt: null,
    claimStatus: null,
    claimFetchedAt: null,
    claim: null,
    comments: [],
    ...overrides,
  }
}

const APPROVED = detail({
  state: 'approved',
  visaNo: 'PLACEHOLDER-VISA-0001',
  submittedAt: '2026-08-22T08:30:00',
  decidedAt: '2026-08-22T09:30:00',
  claimStatus: 'fresh',
  claimFetchedAt: '2026-08-22T09:30:00',
  claim: {
    visaNo: 'PLACEHOLDER-VISA-0001',
    policyNo: 'PLACEHOLDER-POL-01',
    plateNo: 'PLC-TEST-D1',
    insuredName: 'PLACEHOLDER Insured',
    insuredPhone: '+999000009001',
    carMakeModel: 'PLACEHOLDER Make',
    city: 'PLACEHOLDER City',
    accidentDate: '2026-08-12',
  },
  comments: [{ body: 'PLACEHOLDER approved, proceed', createdAt: '2026-08-22T09:30:00' }],
})

/** The rejection the garage is shown — note the server sends **no comments** for it (§1). */
const REJECTED = detail({
  state: 'rejected',
  submittedAt: '2026-08-22T08:30:00',
  decidedAt: '2026-08-22T09:30:00',
  comments: [],
})

describe('G3 — declaration detail', () => {
  let fetchMock: ReturnType<typeof vi.fn>

  beforeEach(() => {
    vi.stubGlobal('URL', {
      ...URL,
      createObjectURL: () => 'blob:PLACEHOLDER',
      revokeObjectURL: vi.fn(),
    })
  })

  describe('the states of §5.2, from the other side', () => {
    it('draft offers both buckets and a Submit button', async () => {
      show(detail(), [DOCUMENT])

      // Awaited on a *capture control*, not on the heading and not on Submit. Both of those render
      // before `GET /api/config/media` has landed, so either would prove the panel exists rather
      // than that it is usable — which is how this test first failed.
      await screen.findAllByLabelText('Take a photo')

      expect(screen.getByRole('heading', { name: /^Survey documents/ })).toBeDefined()
      expect(screen.getByRole('heading', { name: /^Car photos/ })).toBeDefined()

      // §7.1's capture-only rule, expressed by what is rendered: the car-photo panel offers no file
      // picker, the documents panel does.
      const photos = within(
        screen.getByRole('heading', { name: /^Car photos/ }).closest('section')!,
      )
      expect(photos.queryByLabelText('Choose a file')).toBeNull()
      expect(photos.getByLabelText('Take a photo')).toBeDefined()

      const documents = within(
        screen.getByRole('heading', { name: /^Survey documents/ }).closest('section')!,
      )
      expect(documents.getByLabelText('Choose a file')).toBeDefined()
    })

    it('submitted shows a wait and no controls at all', async () => {
      show(detail({ state: 'submitted', submittedAt: '2026-08-22T08:30:00' }), [DOCUMENT])

      expect(await screen.findByText(/with a claim officer/)).toBeDefined()
      expect(screen.queryByRole('button', { name: 'Submit to AXA' })).toBeNull()
      expect(screen.queryByRole('heading', { name: /^Car photos/ })).toBeNull()
      expect(screen.queryByRole('button', { name: 'Start repairs' })).toBeNull()
    })

    it('rejected shows the status and never a comment', async () => {
      // §1, and it will surprise users: the BRD grants comment visibility "in case of confirmation"
      // only. The server sends none either, so this is not a screen hiding what it holds — but if a
      // future DTO started including them, this is what would go red.
      show(REJECTED, [DOCUMENT])

      expect(await screen.findByRole('heading', { name: 'Not accepted' })).toBeDefined()
      expect(screen.getByText(/File a new one/)).toBeDefined()
      expect(screen.queryByText(/PLACEHOLDER approved/)).toBeNull()
      expect(screen.queryByRole('heading', { name: 'AXA comments' })).toBeNull()
      expect(screen.queryByRole('button', { name: 'Start repairs' })).toBeNull()
    })

    it('approved unlocks the claim detail, the comments and Start repairs', async () => {
      show(APPROVED, [DOCUMENT])

      expect(await screen.findByRole('button', { name: 'Start repairs' })).toBeDefined()

      // §5.2: "the garage view of G3 unlocks the full claim detail" — the fields NEXT3 owns.
      expect(screen.getByText('PLACEHOLDER-POL-01')).toBeDefined()
      expect(screen.getByText('+999000009001')).toBeDefined()
      expect(screen.getByText('2026-08-12')).toBeDefined()
      expect(screen.getByText(/PLACEHOLDER approved, proceed/)).toBeDefined()

      // And nothing may be attached any more — the server refuses it too
      // (`409 declaration_already_decided`), because an OnApproval bucket written after the approve
      // transition would never be enqueued by anything.
      expect(screen.queryByLabelText('Take a photo')).toBeNull()
    })

    it('repairs in progress names the slice that will finish it', async () => {
      show(detail({ ...APPROVED, state: 'repairs_in_progress' }), [DOCUMENT])

      // Named rather than left blank: a garage mid-repair will look for where to send the invoice,
      // and "not in this release" is a better answer than an empty screen.
      expect(await screen.findByRole('heading', { name: 'Repairs in progress' })).toBeDefined()
      expect(screen.getByText(/not part of this release/)).toBeDefined()
      expect(screen.queryByRole('button', { name: 'Start repairs' })).toBeNull()
    })
  })

  describe('submitting', () => {
    it('is refused until something has been attached', async () => {
      // The smaller interpretation, applied to the screen only (recorded in scope-decisions.md): a
      // declaration with nothing attached is one an officer can only reject. The server stays
      // permissive, so this is an affordance and not a control.
      show(detail(), [])

      const submit = await screen.findByRole('button', { name: 'Submit to AXA' })
      expect(submit.hasAttribute('disabled')).toBe(true)
      expect(screen.getByText(/Add at least one photo or document/)).toBeDefined()
    })

    it('sends exactly one request however fast the button is pressed', async () => {
      // 1.5's lesson, sixth outing. `mutation.isPending` cannot stop two clicks in the same tick —
      // React has not re-rendered — so the guard is a useRef latch. Remove it from
      // `useDeclarationAction` and this goes red.
      show(detail(), [DOCUMENT])

      // Enabled, not merely present: the button renders while the document list is still in flight,
      // and a disabled button swallows every click without sending anything — which would make this
      // test pass for entirely the wrong reason.
      const submit = await screen.findByRole('button', { name: 'Submit to AXA' })
      await waitFor(() => {
        expect(submit.hasAttribute('disabled')).toBe(false)
      })

      // **Three clicks inside one `act`, deliberately.** `fireEvent` flushes React between events, so
      // by its second call `pending` is already true and the `disabled` attribute swallows the rest —
      // which is a real guard, but not the one under test, and a version of this test using
      // `fireEvent` stayed green with the latch deleted. Dispatched raw and unflushed, all three
      // handlers observe `pending === false`, which is exactly two taps in the same tick on a phone.
      await act(async () => {
        submit.click()
        submit.click()
        submit.click()
      })

      await waitFor(() => {
        expect(submitCalls()).toBe(1)
      })
    })
  })

  function submitCalls(): number {
    return fetchMock.mock.calls.filter(([url, init]) => {
      const request = init as RequestInit | undefined
      return String(url).endsWith('/submit') && request?.method === 'POST'
    }).length
  }

  function show(body: DeclarationDetail, documents: DeclarationDocument[]) {
    fetchMock = vi.fn((url: string, init?: RequestInit) => {
      const path = String(url)
      if (path.endsWith('/api/config/media')) return Promise.resolve(json(MEDIA_CONFIG))
      if (path.endsWith('/submit') || path.endsWith('/start-repairs')) {
        return Promise.resolve(json({ state: 'submitted' as DeclarationState }))
      }
      if (path.endsWith('/documents')) return Promise.resolve(json(documents))
      void init
      return Promise.resolve(json(body))
    })
    vi.stubGlobal('fetch', fetchMock)

    render(
      <TestQueryProvider>
        <MemoryRouter initialEntries={[`/garage/${ID}`]}>
          <Routes>
            <Route path="/garage/:id" element={<GarageDeclarationPage />} />
          </Routes>
        </MemoryRouter>
      </TestQueryProvider>,
    )
  }
})

function json(body: unknown): Response {
  return new Response(JSON.stringify(body), { status: 200 })
}
