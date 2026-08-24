import { act, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { DeclarationDetail, DeclarationDocument, DeclarationState } from '../garage/api'
import type { MediaConfig } from '../media/config'
import { TestQueryProvider } from '../testing/TestQueryProvider'
import GarageDeclarationPage from './GarageDeclarationPage'

const ID = '00000000-0000-0000-0000-0000000d0001'

/**
 * §7.1's garage buckets as `GET /api/config/media` serves them — the two from slice 4.1 and the
 * three G4 ones from 5.1. The endpoint projects `MediaBuckets` wholesale, so a bucket missing here
 * is a bucket whose panel renders "not configured for uploads yet" instead of a control.
 */
const MEDIA_CONFIG: MediaConfig = {
  clarity: { minWidth: 1024, minHeight: 768, blurVarianceThreshold: 100, blurAnalysisMaxEdge: 512 },
  maxFileMb: 15,
  buckets: [
    { bucket: 'garage_documents', allowUpload: true, contentTypes: ['image/jpeg', 'application/pdf'] },
    // Capture-only, the BRD's hard rule — so the panel must render no file picker at all.
    { bucket: 'garage_car_photo', allowUpload: false, contentTypes: ['image/jpeg'] },
    // G4: the repair photo is capture-only for the same reason; the paperwork is not.
    { bucket: 'repair_photo', allowUpload: false, contentTypes: ['image/jpeg'] },
    { bucket: 'discharge', allowUpload: true, contentTypes: ['image/jpeg', 'application/pdf'] },
    { bucket: 'invoice', allowUpload: true, contentTypes: ['image/jpeg', 'application/pdf'] },
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
  pushConfirmed: false,
  blobRetained: true,
  createdAt: '2026-08-22T09:00:00',
}

const INVOICE: DeclarationDocument = {
  ...DOCUMENT,
  id: '00000000-0000-0000-0000-0000000dd002',
  bucket: 'invoice',
  docType: 'PLACEHOLDER-DOC-11',
  origin: 'uploaded',
  contentType: 'application/pdf',
  fileName: 'PLACEHOLDER-invoice.pdf',
  pushStatus: 'queued',
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
    repairDocsSubmittedAt: null,
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

const REPAIRING = detail({
  ...APPROVED,
  state: 'repairs_in_progress',
  repairsStartedAt: '2026-08-22T10:00:00',
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

    it('repairs in progress offers the three G4 buckets', async () => {
      // **Rewritten in slice 5.1**, and the assertion it replaced was the honest one for 4.2: it
      // pinned a placeholder naming this slice. What it checked no longer exists on the screen.
      show(REPAIRING, [DOCUMENT])

      await screen.findAllByLabelText('Take a photo')

      expect(screen.getByRole('heading', { name: /^Repair photos/ })).toBeDefined()
      expect(screen.getByRole('heading', { name: /^Discharge/ })).toBeDefined()
      expect(screen.getByRole('heading', { name: /^Invoice/ })).toBeDefined()
      expect(screen.queryByRole('button', { name: 'Start repairs' })).toBeNull()

      // §7.1's capture-only rule again, and the split that makes these three buckets rather than
      // one: the repair photo offers no file picker, the paperwork does.
      const photos = within(
        screen.getByRole('heading', { name: /^Repair photos/ }).closest('section')!,
      )
      expect(photos.queryByLabelText('Choose a file')).toBeNull()
      expect(photos.getByLabelText('Take a photo')).toBeDefined()

      const invoice = within(screen.getByRole('heading', { name: /^Invoice/ }).closest('section')!)
      expect(invoice.getByLabelText('Choose a file')).toBeDefined()
    })

    it('repair documents show that they are already on their way to AXA', async () => {
      // The first garage screen where `PushIndicator` says anything: these buckets are Immediate, so
      // a repair document is `queued` at upload rather than waiting for a transition. Before
      // approval every garage row is `deferred` and the indicator deliberately renders nothing.
      show(REPAIRING, [DOCUMENT, INVOICE])

      expect(await screen.findByText('Queued, will send')).toBeDefined()
    })

    it('and say so once NEXT3 has acknowledged them', async () => {
      // **`pushConfirmed`, not `pushStatus === 'sent'`.** The document row never says "sent" — §4
      // keeps live push state on the outbox row — so the indicator asked a question the API cannot
      // answer, and this string never appeared on any screen until slice 5.1 computed it at read
      // time. Found by the manual pass, which is the only place it could have been.
      show(REPAIRING, [DOCUMENT, { ...INVOICE, pushConfirmed: true }])

      expect(await screen.findByText('Sent to AXA')).toBeDefined()
      expect(screen.queryByText('Queued, will send')).toBeNull()
    })

    it('the last step is refused until something from the repair exists', async () => {
      // "At least one repair document, any bucket" — pass-2 review decision 5. The artboard proposed
      // invoice-only, which the BRD ("documents such like discharge, invoice") does not support, so
      // a declaration carrying only its pre-approval car photo is not finishable.
      show(REPAIRING, [DOCUMENT])

      const submit = await screen.findByRole('button', { name: 'Submit repair documents' })
      expect(submit.hasAttribute('disabled')).toBe(true)
      expect(screen.getByText(/Add at least one repair document/)).toBeDefined()
    })

    it('repair documents sent is terminal and shows the four timestamps', async () => {
      show(
        detail({
          ...APPROVED,
          state: 'repair_docs_submitted',
          repairsStartedAt: '2026-08-22T10:00:00',
          repairDocsSubmittedAt: '2026-08-22T11:00:00',
        }),
        [DOCUMENT, INVOICE],
      );

      expect(await screen.findByRole('heading', { name: 'Repair documents sent' })).toBeDefined()
      expect(screen.getByText(/Nothing further is needed from the garage/)).toBeDefined()

      // G4Submitted's timeline. Four labels, and no control of any kind — §5.2 defines no state
      // after this one, so there is nothing to offer.
      expect(screen.getByText('Repairs started')).toBeDefined()
      expect(screen.getByText('Documents sent')).toBeDefined()
      expect(screen.queryByRole('button')).toBeNull()
      expect(screen.queryByLabelText('Take a photo')).toBeNull()
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

    it('finishes the repair with exactly one request however fast the button is pressed', async () => {
      // The same latch on the terminal transition, and the one place it matters most: a second
      // `submit-repair-docs` cannot succeed (the server's `state` token refuses it), but it renders
      // a 409 the garage never caused on the last screen of the flow.
      show(REPAIRING, [DOCUMENT, INVOICE])

      const submit = await screen.findByRole('button', { name: 'Submit repair documents' })
      await waitFor(() => {
        expect(submit.hasAttribute('disabled')).toBe(false)
      })

      await act(async () => {
        submit.click()
        submit.click()
        submit.click()
      })

      await waitFor(() => {
        expect(repairSubmitCalls()).toBe(1)
      })
    })
  })

  function repairSubmitCalls(): number {
    return postsTo('/submit-repair-docs')
  }

  function submitCalls(): number {
    return postsTo('/submit')
  }

  function postsTo(suffix: string): number {
    return fetchMock.mock.calls.filter(([url, init]) => {
      const request = init as RequestInit | undefined
      return String(url).endsWith(suffix) && request?.method === 'POST'
    }).length
  }

  function show(body: DeclarationDetail, documents: DeclarationDocument[]) {
    fetchMock = vi.fn((url: string, init?: RequestInit) => {
      const path = String(url)
      if (path.endsWith('/api/config/media')) return Promise.resolve(json(MEDIA_CONFIG))
      if (path.endsWith('/submit-repair-docs')) {
        return Promise.resolve(json({ state: 'repair_docs_submitted' as DeclarationState }))
      }
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
