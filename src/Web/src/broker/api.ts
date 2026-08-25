import { api } from '../api/client'

/** The seven states of design.md §5.3, as the server spells them (`BrokerRequestStates`). */
export type BrokerRequestState =
  | 'draft'
  | 'submitted'
  | 'link_issued'
  | 'customer_in_progress'
  | 'ready_to_send'
  | 'sent'
  | 'expired'

/**
 * One row of B1's worklist (§5.3).
 *
 * Three of the seven states belong to Option 2 requests that exist before anybody has typed anything,
 * so **most of these fields are legitimately null** — the row is real and the fields are empty, which
 * is the rule the B1 artboard states out loud.
 */
export interface BrokerRequestListItem {
  id: string
  option: number
  state: BrokerRequestState
  insuredName: string | null
  insuranceType: string | null
  customerMobile: string | null
  createdAt: string
  submittedAt: string | null
  /**
   * Null on a `submitted` row means the routed email has **not** gone (§5.3's ordering): the state
   * commits before the send, so this is the difference between "filed and delivered" and "filed,
   * delivery still owed". B1 offers Resend on exactly that case.
   */
  emailedAt: string | null
  emailRecipient: string | null
  linkExpiresAt: string | null
  documentCount: number
}

/** B2's detail, and B3's "Afterwards" card. The raw link is never here — only its expiry. */
export interface BrokerRequestDetail {
  id: string
  option: number
  state: BrokerRequestState
  insuredName: string | null
  insuranceType: string | null
  insuredAddress: string | null
  carValue: number | null
  estimatedPremium: number | null
  effectiveDate: string | null
  customerMobile: string | null
  createdAt: string
  submittedAt: string | null
  emailedAt: string | null
  emailRecipient: string | null
  linkExpiresAt: string | null
}

/** A broker document as B2 lists it — the API's shared `DocumentDto`. */
export interface BrokerDocument {
  id: string
  bucket: string
  docType: string | null
  origin: 'captured' | 'uploaded'
  clarityResult: string
  contentType: string
  fileName: string | null
  sizeBytes: number
  pushStatus: string
  pushConfirmed: boolean
  blobRetained: boolean
  createdAt: string
}

/** The server's answer to submit and resend. */
export interface BrokerActionResult {
  state: BrokerRequestState
  /**
   * True when the request is filed but the email did not leave. **Not an error** — the transition
   * happened, and the screen says both things rather than reporting a delivery that did not occur.
   */
  emailFailed: boolean
  recipient: string | null
}

/** B3's answer. The raw token is shown once and never stored — only its hash reaches the database. */
export interface CreatedLink {
  requestId: string
  token: string
  /** The API path (`/public/{token}`). The screen renders `{appBaseUrl}/p/{token}` instead. */
  url: string
  expiresAt: string
}

/** `garageKeys`' shape and its reasoning: `lists()` is what "the list, whichever one" invalidates. */
export const brokerKeys = {
  all: ['broker', 'requests'] as const,
  lists: () => [...brokerKeys.all, 'list'] as const,
  list: () => [...brokerKeys.lists(), ''] as const,
  detail: (requestId: string) => [...brokerKeys.all, 'detail', requestId] as const,
  documents: (requestId: string) => [...brokerKeys.all, 'documents', requestId] as const,
}

/** B2's upload target, and the path its capture panel posts to. */
export function requestDocumentsPath(requestId: string): string {
  return `/api/broker/requests/${requestId}/documents`
}

/**
 * B4's photo and document previews (slice 6.1). The *fetch* points here and only the rendered `src`
 * is a `blob:` URL — a browser cannot authenticate an `<img src>`, and an unauthenticated one would
 * 401 and hard-navigate the broker to `/login` mid-review. See `media/useDocumentBlobUrl`.
 */
export function requestDocumentContentPath(requestId: string, documentId: string): string {
  return `${requestDocumentsPath(requestId)}/${documentId}/content`
}

export function listRequests(signal?: AbortSignal): Promise<BrokerRequestListItem[]> {
  return api<BrokerRequestListItem[]>('/api/broker/requests', { signal })
}

export function getRequest(requestId: string, signal?: AbortSignal): Promise<BrokerRequestDetail> {
  return api<BrokerRequestDetail>(`/api/broker/requests/${requestId}`, { signal })
}

export function listRequestDocuments(
  requestId: string,
  signal?: AbortSignal,
): Promise<BrokerDocument[]> {
  return api<BrokerDocument[]>(requestDocumentsPath(requestId), { signal })
}

/** §5.3's six fields. All six are required by the server — there is no partial save. */
export interface CreateRequestBody {
  insuredName: string
  insuranceType: string
  insuredAddress: string
  carValue: number
  estimatedPremium: number
  effectiveDate: string
}

export function createRequest(body: CreateRequestBody): Promise<BrokerRequestDetail> {
  return api<BrokerRequestDetail>('/api/broker/requests', {
    method: 'POST',
    body: JSON.stringify(body),
  })
}

export function submitRequest(requestId: string): Promise<BrokerActionResult> {
  return api<BrokerActionResult>(`/api/broker/requests/${requestId}/submit`, { method: 'POST' })
}

export function resendRequest(requestId: string): Promise<BrokerActionResult> {
  return api<BrokerActionResult>(`/api/broker/requests/${requestId}/resend`, { method: 'POST' })
}

/**
 * B4's Send email (slice 5.3) — Option 2's `ready_to_send` -> `sent`.
 *
 * Its own route rather than `submit`: the broker is releasing a submission somebody else filled in,
 * and the server walks a different edge for it. `resend` is not it either — that one refuses
 * `ready_to_send` outright, on purpose, so a customer's file cannot be mailed past this review.
 */
export function sendRequest(requestId: string): Promise<BrokerActionResult> {
  return api<BrokerActionResult>(`/api/broker/requests/${requestId}/send`, { method: 'POST' })
}

/**
 * Appendix A's `Broker:InsuranceTypes` (#14), over the wire.
 *
 * Never a list in this file: the real types are a client answer, and CLAUDE.md's placeholder rule
 * makes a client literal outside `appsettings.Placeholders.json` a bug wherever it appears. It is
 * also what the server validates a submission against, so a second copy here would eventually offer
 * a broker a type the server refuses.
 */
export interface BrokerConfig {
  insuranceTypes: string[]
  /**
   * #13's routing table (slice 5.3). B4 names the desk a submission will reach **before** the broker
   * presses Send — `emailRecipient` is written *by* the send, so it is null on the one screen where
   * seeing the address would let somebody notice a wrong route.
   */
  emailRouting: Record<string, string>
}

export function fetchBrokerConfig(signal?: AbortSignal): Promise<BrokerConfig> {
  return api<BrokerConfig>('/api/broker/config', { signal })
}

/** B3 (§5.3). The insurance type is an optional preset; "let the customer choose" sends null. */
export function createLink(body: {
  customerMobile: string | null
  insuranceType: string | null
}): Promise<CreatedLink> {
  return api<CreatedLink>('/api/broker/link-requests', {
    method: 'POST',
    body: JSON.stringify(body),
  })
}
