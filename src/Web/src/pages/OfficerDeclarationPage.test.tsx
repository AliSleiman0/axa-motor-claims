import { render, screen, waitFor, within } from '@testing-library/react'
import { fireEvent } from '@testing-library/dom'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { DeclarationDocument } from '../garage/api'
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
  note: 'PLACEHOLDER note',
  visaNo: null,
  createdAt: '2026-08-22T08:00:00',
  submittedAt: '2026-08-22T08:30:00',
  decidedAt: null,
  garageName: 'PLACEHOLDER Garage Contact',
  garageEmail: 'PLACEHOLDER-garage@example.invalid',
  garagePhone: '+999000008001',
  comments: [],
}

const PHOTO: DeclarationDocument = {
  id: '00000000-0000-0000-0000-0000000dd001',
  bucket: 'garage_car_photo',
  docType: 'PLACEHOLDER-DOC-13',
  origin: 'captured',
  clarityResult: 'passed',
  contentType: 'image/jpeg',
  fileName: 'PLACEHOLDER-photo.jpg',
  sizeBytes: 2048,
  pushStatus: 'deferred',
  pushConfirmed: false,
  blobRetained: true,
  createdAt: '2026-08-22T09:00:00',
}

const INVOICE: DeclarationDocument = {
  ...PHOTO,
  id: '00000000-0000-0000-0000-0000000dd002',
  bucket: 'garage_documents',
  contentType: 'application/pdf',
  fileName: 'invoice.pdf',
}

/** §7.3 swept the bytes once NEXT3 confirmed the push. The row survives; the file does not. */
const SWEPT: DeclarationDocument = {
  ...PHOTO,
  id: '00000000-0000-0000-0000-0000000dd003',
  fileName: 'PLACEHOLDER-sent.jpg',
  pushStatus: 'queued',
  blobRetained: false,
}

const ME = { id: 'u1', phone: '+999000000009', role: 'claim_officer', displayName: 'PLACEHOLDER Officer' }

describe('O2 — declaration review', () => {
  let calls: string[]
  let fetchMock: ReturnType<typeof vi.fn>

  beforeEach(() => {
    vi.stubGlobal('URL', {
      ...URL,
      createObjectURL: () => 'blob:PLACEHOLDER',
      revokeObjectURL: vi.fn(),
    })
  })

  describe('the approve sequence', () => {
    it('renders, uploads, then approves — in that order', async () => {
      // §5.2's approval is two calls for exactly this reason: #18's decision image has to exist
      // before the declaration moves, and the server refuses an approval without one
      // (`409 approval_image_required`). The order below is the client half of that agreement.
      const renderPng = vi.fn(() => Promise.resolve(new Blob([new Uint8Array([1, 2, 3])])))
      show({ render: renderPng })

      await approve()

      await waitFor(() => {
        expect(calls).toContain(`POST /api/officer/declarations/${ID}/approve`)
      })

      expect(renderPng).toHaveBeenCalledTimes(1)
      const upload = calls.indexOf(`POST /api/officer/declarations/${ID}/documents`)
      const approveCall = calls.indexOf(`POST /api/officer/declarations/${ID}/approve`)
      expect(upload).toBeGreaterThanOrEqual(0)
      expect(upload).toBeLessThan(approveCall)
    })

    it('sends the image as an approval_image, captured, with the metadata parts first', async () => {
      show({ render: () => Promise.resolve(new Blob([new Uint8Array([1])])) })

      await approve()
      await waitFor(() => {
        expect(uploadBody()).toBeDefined()
      })

      const body = uploadBody()!
      // The order is the wire contract: the upload is streamed, so the server must know the bucket
      // before the bytes reach storage (`400 metadata_must_precede_file` otherwise).
      expect([...body.keys()]).toEqual(['bucket', 'origin', 'file'])
      expect(body.get('bucket')).toBe('approval_image')
      expect(body.get('origin')).toBe('captured')
      expect((body.get('file') as File).name).toBe(`approval-${ID}.png`)
    })

    it('does not approve when the upload fails', async () => {
      // §5.2's "a render failure aborts the approval cleanly", from the upload side. If approve ran
      // anyway the server would refuse it — but the officer would see a 409 they did not cause, and
      // a retry would be their only clue.
      show({
        render: () => Promise.resolve(new Blob([new Uint8Array([1])])),
        uploadStatus: 413,
      })

      await approve()

      await waitFor(() => {
        expect(screen.getByRole('alert')).toBeDefined()
      })
      expect(calls).not.toContain(`POST /api/officer/declarations/${ID}/approve`)
    })

    it('does not upload or approve when the render fails', async () => {
      show({ render: () => Promise.reject(new Error('canvas said no')) })

      await approve()

      await waitFor(() => {
        expect(screen.getByRole('alert')).toBeDefined()
      })
      expect(calls).not.toContain(`POST /api/officer/declarations/${ID}/documents`)
      expect(calls).not.toContain(`POST /api/officer/declarations/${ID}/approve`)
    })

    it('refuses to approve without a visa, before anything is rendered', async () => {
      // The visa is what every deferred document is pushed under. Approving without one would render
      // and upload an image naming no claim, then be refused by the server.
      const renderPng = vi.fn(() => Promise.resolve(new Blob([new Uint8Array([1])])))
      show({ render: renderPng })

      fireEvent.click(await screen.findByRole('button', { name: 'Approve' }))

      await waitFor(() => {
        expect(screen.getByRole('alert').textContent).toContain('visa number')
      })
      expect(renderPng).not.toHaveBeenCalled()
      expect(calls).not.toContain(`POST /api/officer/declarations/${ID}/documents`)
    })
  })

  describe('the visa search', () => {
    it('offers a hit and puts it in the decision field', async () => {
      show({})

      fireEvent.change(await screen.findByLabelText('Plate'), { target: { value: 'PLC-TEST-D1' } })
      fireEvent.click(screen.getByRole('button', { name: 'Search' }))

      const use = await screen.findByRole('button', { name: 'Use this visa' })
      fireEvent.click(use)

      expect((screen.getByLabelText('Visa number') as HTMLInputElement).value).toBe(VISA)
    })

    it('gives the #16 answer when nothing matches', async () => {
      // §5.2's officer-review row: there is no create-visa API and this release does not build one,
      // so the officer leaves, creates the visa in NEXT3 and searches again. Saying nothing would
      // leave them stuck on a screen with no next step.
      show({ searchResults: [] })

      fireEvent.change(await screen.findByLabelText('Visa'), { target: { value: 'PLACEHOLDER-NOPE' } })
      fireEvent.click(screen.getByRole('button', { name: 'Search' }))

      await waitFor(() => {
        expect(screen.getByRole('alert').textContent).toContain('Create the visa in NEXT3')
      })
    })

    it('will not search with neither term', async () => {
      show({})

      fireEvent.click(await screen.findByRole('button', { name: 'Search' }))

      await waitFor(() => {
        expect(screen.getByRole('alert').textContent).toContain('plate number or a visa number')
      })
      // §6.1: "neither term supplied returns empty, never every claim" — and the screen does not
      // even ask.
      expect(calls.some((call) => call.includes('/claims/search'))).toBe(false)
    })
  })

  describe('the documents', () => {
    it('previews an image from the content endpoint, as a blob URL', async () => {
      show({ documents: [PHOTO] })

      const image = await screen.findByAltText(`Document ${PHOTO.fileName}`)

      // The **fetch** is what points at the content endpoint; the `src` is a blob: URL, because a
      // browser sends no Authorization header for an `<img src>` and the resulting 401 would sign
      // the officer out mid-review.
      expect(image.getAttribute('src')).toBe('blob:PLACEHOLDER')
      expect(calls).toContain(
        `GET /api/officer/declarations/${ID}/documents/${PHOTO.id}/content`,
      )
    })

    it('names a PDF instead of rendering it, and fetches it only on request', async () => {
      // 2.5's lesson: a PDF in an `<img>` is a broken-image icon over alt text calling it a photo,
      // and nothing throws — `naturalWidth` is simply 0 and every assertion still passes.
      show({ documents: [INVOICE] })

      const open = await screen.findByRole('button', { name: 'Open invoice.pdf' })
      expect(screen.queryByAltText(/Document invoice\.pdf/)).toBeNull()

      // Not fetched until asked: a claim with a dozen photographs and three PDFs should not pull the
      // PDFs before anyone wants them.
      expect(calls).not.toContain(
        `GET /api/officer/declarations/${ID}/documents/${INVOICE.id}/content`,
      )

      fireEvent.click(open)

      await waitFor(() => {
        expect(
          calls.includes(`GET /api/officer/declarations/${ID}/documents/${INVOICE.id}/content`),
        ).toBe(true)
      })
      const link = await screen.findByRole('link', { name: 'Open invoice.pdf' })
      expect(link.getAttribute('href')).toBe('blob:PLACEHOLDER')
      expect(link.getAttribute('download')).toBe('invoice.pdf')
    })

    it('says a swept document is gone rather than linking it', async () => {
      show({ documents: [SWEPT] })

      const row = within(
        (await screen.findByRole('heading', { name: /PLACEHOLDER-sent\.jpg/ })).closest('section')!,
      )

      expect(row.getByText(/local copy has been removed/)).toBeDefined()
      expect(row.queryByRole('img')).toBeNull()
      expect(row.queryByRole('button')).toBeNull()
      // Never even attempted — the list already says `blobRetained: false`, so a request here would
      // be a 404 the screen asked for.
      expect(calls).not.toContain(
        `GET /api/officer/declarations/${ID}/documents/${SWEPT.id}/content`,
      )
    })
  })

  async function approve() {
    fireEvent.change(await screen.findByLabelText('Visa number'), { target: { value: VISA } })
    fireEvent.change(screen.getByLabelText('Comments'), {
      target: { value: 'PLACEHOLDER approved' },
    })
    fireEvent.click(screen.getByRole('button', { name: 'Approve' }))
  }

  function uploadBody(): FormData | undefined {
    const call = fetchMock.mock.calls.find(([url, init]) => {
      const request = init as RequestInit | undefined
      return String(url).endsWith('/documents') && request?.method === 'POST'
    })
    return call ? ((call[1] as RequestInit).body as FormData) : undefined
  }

  function show({
    documents = [],
    searchResults = [{ visaNo: VISA, plateNo: 'PLC-TEST-D1', insuredName: 'PLACEHOLDER Insured' }],
    uploadStatus = 201,
    render: renderPng,
  }: {
    documents?: DeclarationDocument[]
    searchResults?: { visaNo: string; plateNo: string; insuredName: string }[]
    uploadStatus?: number
    render?: (markup: string, size: { width: number; height: number }) => Promise<Blob>
  }) {
    calls = []
    fetchMock = vi.fn((url: string, init?: RequestInit) => {
      const path = String(url)
      const method = init?.method ?? 'GET'
      // The query string is stripped so a search is one recognisable entry however it was spelled.
      calls.push(`${method} ${path.split('?')[0]}`)

      if (path.includes('/claims/search')) return Promise.resolve(json(searchResults))
      if (path.endsWith('/auth/me')) return Promise.resolve(json(ME))
      if (path.endsWith('/content')) {
        return Promise.resolve(new Response(new Blob([new Uint8Array([1, 2])]), { status: 200 }))
      }
      if (path.endsWith('/documents')) {
        return method === 'POST'
          ? Promise.resolve(
              uploadStatus === 201
                ? json({ id: 'doc-approval' }, 201)
                : new Response(JSON.stringify({ error: 'file_too_large' }), { status: uploadStatus }),
            )
          : Promise.resolve(json(documents))
      }
      if (path.endsWith('/approve') || path.endsWith('/reject')) {
        return Promise.resolve(json({ state: 'approved' }))
      }
      return Promise.resolve(json(DETAIL))
    })
    vi.stubGlobal('fetch', fetchMock)

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
