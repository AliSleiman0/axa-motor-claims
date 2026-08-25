import { ApiError } from '../api/client'

/**
 * design.md §9.1's `GET /public/{token}` — deliberately almost nothing.
 *
 * `brokerDisplayName` is the one identifying value, and it is null on any link issued before slice
 * 5.2 snapshotted the column. The page shows no name at all in that case rather than a placeholder:
 * a form that says "PLACEHOLDER Broker asked you to complete this" is worse than one that says
 * nothing, because it looks like a bug on the one screen that has to look trustworthy.
 */
export interface PublicLinkView {
  state: string
  expiresAt: string
  maxFiles: number
  maxFileMb: number
  brokerDisplayName: string | null
  /** #14's list, served here because `/api/broker/config` is behind a policy P1 has no session for. */
  insuranceTypes: string[]
}

/** What the customer sees of a file they have attached: enough to recognise it, and nothing else. */
export interface PublicDocument {
  id: string
  bucket: string
  fileName: string | null
  sizeBytes: number
}

/** The six fields of §5.3, including the customer-entered premium (§1's decision, flagged #24c). */
export interface PublicSubmission {
  insuredName: string
  insuranceType: string
  insuredAddress: string
  carValue: number
  estimatedPremium: number
  effectiveDate: string
}

export const PUBLIC_DOCUMENT_BUCKET = 'public_document'

export function publicLinkPath(token: string): string {
  return `/public/${encodeURIComponent(token)}`
}

export function publicDocumentsPath(token: string): string {
  return `${publicLinkPath(token)}/documents`
}

/**
 * The public page's own client, and the reason it exists is worth stating rather than assuming.
 *
 * `api()` and `apiBlob()` are for signed-in staff: they attach a bearer whenever `localStorage`
 * holds one, and on a 401 they refresh once and then **hard-navigate to `/login`**. Both behaviours
 * are wrong here. A broker checking their own customer's link *does* have tokens, so the header
 * would be attached; and a member of the public who lands on a dead link must see "this link is no
 * longer open", not a staff sign-in screen they have no account for.
 *
 * So: a bare `fetch`, no credentials of any kind, and `ApiError` — the shared type — thrown on
 * anything that is not 2xx, so callers branch on `status` exactly as they do elsewhere.
 * `reusability.test.ts` holds this module to it by forbidding `api/tokens`, `api/session` and
 * `api/client`'s request helpers outright.
 */
async function send(path: string, init: RequestInit = {}): Promise<string> {
  const response = await fetch(path, init)
  const body = await response.text()
  if (!response.ok) throw new ApiError(response.status, body)
  return body
}

async function json<T>(path: string, init: RequestInit = {}): Promise<T> {
  const body = await send(path, init)
  return (body ? JSON.parse(body) : undefined) as T
}

export function fetchPublicLink(token: string, signal?: AbortSignal): Promise<PublicLinkView> {
  return json<PublicLinkView>(publicLinkPath(token), { signal })
}

export function fetchPublicDocuments(
  token: string,
  signal?: AbortSignal,
): Promise<PublicDocument[]> {
  return json<PublicDocument[]>(publicDocumentsPath(token), { signal })
}

export async function submitPublicRequest(
  token: string,
  submission: PublicSubmission,
): Promise<void> {
  await send(`${publicLinkPath(token)}/submit`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(submission),
  })
}

export const publicKeys = {
  all: ['public'] as const,
  link: (token: string) => [...publicKeys.all, 'link', token] as const,
  documents: (token: string) => [...publicKeys.all, 'documents', token] as const,
}

/**
 * The refusals §5.3's submit can answer, in words a member of the public can act on.
 *
 * Every one of them leaves the link alive (1.5's rule), which is why each message says what to
 * change rather than apologising — the customer fixes it and presses Send again.
 */
const SUBMIT_EXPLANATIONS: Record<string, string> = {
  incomplete_submission: 'Some details are still missing. Fill in every field and try again.',
  unknown_insurance_type: 'That insurance type is no longer offered. Choose one from the list.',
  documents_required: 'Attach at least one supporting document before sending.',
  // Slice 6.1. The server does not name the missing side — §9.1's surface says as little as it can —
  // so the sentence points at the checklist, which is computed from the page's own document list and
  // already shows exactly which ones are still needed.
  car_photos_required:
    'All five photographs of the car are needed. The list above shows which are still missing.',
}

export function describeSubmitError(error: unknown): string {
  if (error instanceof ApiError) {
    try {
      const parsed: unknown = JSON.parse(error.body)
      if (typeof parsed === 'object' && parsed !== null && 'error' in parsed) {
        const code = (parsed as { error: unknown }).error
        if (typeof code === 'string' && code in SUBMIT_EXPLANATIONS) {
          return SUBMIT_EXPLANATIONS[code]
        }
      }
    } catch {
      // Not a coded body — fall through to the generic message.
    }
  }
  return 'This could not be sent. Check your connection and try again.'
}
