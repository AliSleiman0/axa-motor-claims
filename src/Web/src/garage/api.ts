import { api } from '../api/client'

/** The six states of design.md §5.2, as the server spells them (`DeclarationStates`). */
export type DeclarationState =
  | 'draft'
  | 'submitted'
  | 'approved'
  | 'rejected'
  | 'repairs_in_progress'
  | 'repair_docs_submitted'

/** One row of G1's worklist (§5.2). */
export interface DeclarationListItem {
  id: string
  state: DeclarationState
  plateNo: string
  insuredName: string | null
  visaNo: string | null
  createdAt: string
  submittedAt: string | null
  decidedAt: string | null
  mediaCount: number
}

/** The claim as NEXT3 owns it — the same shape E2 shows an expert (§6.1). */
export interface Claim {
  visaNo: string
  policyNo: string
  plateNo: string
  insuredName: string
  insuredPhone: string
  carMakeModel: string
  city: string
  accidentDate: string
}

export interface DeclarationComment {
  body: string
  createdAt: string
}

/**
 * G3 (§5.2).
 *
 * `claim` and `comments` are **both empty until the declaration is approved**, and that is the
 * server's doing rather than the screen's: §5.2 unlocks the claim detail on approval, and the BRD
 * grants comment visibility "in case of confirmation" only, so a rejected declaration's comments
 * never leave the database at all. The screen renders what it is given; it does not filter.
 */
export interface DeclarationDetail {
  id: string
  state: DeclarationState
  plateNo: string
  insuredName: string | null
  note: string | null
  visaNo: string | null
  createdAt: string
  submittedAt: string | null
  decidedAt: string | null
  repairsStartedAt: string | null
  repairDocsSubmittedAt: string | null
  claimStatus: 'fresh' | 'stale' | 'not_found' | null
  claimFetchedAt: string | null
  claim: Claim | null
  comments: DeclarationComment[]
}

/** A document as both views list it — the API's shared `DocumentDto`. */
export interface DeclarationDocument {
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
  /** False once §7.3's sweep has deleted the bytes — the screens link nothing that is false. */
  blobRetained: boolean
  createdAt: string
}

/**
 * Query keys are the invalidation contract, and `lists()` is deliberate rather than tidy.
 *
 * There is no search on G1 today, so `list()` and `lists()` currently name the same key — and the
 * split is here anyway, because slice 3.2 learned this the expensive way on the expert side: the day
 * a filter goes into the list key, every `invalidateQueries` that named `list()` silently stops
 * matching the list actually on screen, and every existing test stays green. Anything invalidating
 * "the list, whichever one is showing" invalidates `lists()`.
 */
export const garageKeys = {
  all: ['garage', 'declarations'] as const,
  lists: () => [...garageKeys.all, 'list'] as const,
  list: () => [...garageKeys.lists(), ''] as const,
  detail: (declarationId: string) => [...garageKeys.all, 'detail', declarationId] as const,
  documents: (declarationId: string) => [...garageKeys.all, 'documents', declarationId] as const,
}

/** G3's upload target, and the path the capture panels post to. */
export function declarationDocumentsPath(declarationId: string): string {
  return `/api/garage/declarations/${declarationId}/documents`
}

/**
 * Where a document's bytes come from (slice 4.2).
 *
 * Fetched through `apiBlob` and turned into an object URL — never used as an `<img src>` directly,
 * because a browser sends no Authorization header for one and the 401 would sign the garage out.
 */
export function documentContentPath(declarationId: string, documentId: string): string {
  return `${declarationDocumentsPath(declarationId)}/${documentId}/content`
}

export function listDeclarations(signal?: AbortSignal): Promise<DeclarationListItem[]> {
  return api<DeclarationListItem[]>('/api/garage/declarations', { signal })
}

export function getDeclaration(
  declarationId: string,
  signal?: AbortSignal,
): Promise<DeclarationDetail> {
  return api<DeclarationDetail>(`/api/garage/declarations/${declarationId}`, { signal })
}

export function listDeclarationDocuments(
  declarationId: string,
  signal?: AbortSignal,
): Promise<DeclarationDocument[]> {
  return api<DeclarationDocument[]>(declarationDocumentsPath(declarationId), { signal })
}

export interface CreateDeclarationRequest {
  plateNo: string
  insuredName: string | null
  note: string | null
}

export function createDeclaration(
  request: CreateDeclarationRequest,
): Promise<DeclarationListItem> {
  return api<DeclarationListItem>('/api/garage/declarations', {
    method: 'POST',
    body: JSON.stringify(request),
  })
}

/** The two transitions a garage owns (§5.2). The server answers `{ state }`. */
export function submitDeclaration(declarationId: string): Promise<{ state: DeclarationState }> {
  return api<{ state: DeclarationState }>(
    `/api/garage/declarations/${declarationId}/submit`,
    { method: 'POST' },
  )
}

export function startRepairs(declarationId: string): Promise<{ state: DeclarationState }> {
  return api<{ state: DeclarationState }>(
    `/api/garage/declarations/${declarationId}/start-repairs`,
    { method: 'POST' },
  )
}

/**
 * §5.2's terminal transition (G4). Refused with `repair_documents_required` when the declaration
 * carries nothing from the three repair buckets — the screen disables the button for the same
 * reason, but the screen's copy of the document list can be a moment behind the server's.
 */
export function submitRepairDocs(declarationId: string): Promise<{ state: DeclarationState }> {
  return api<{ state: DeclarationState }>(
    `/api/garage/declarations/${declarationId}/submit-repair-docs`,
    { method: 'POST' },
  )
}
