import { api } from '../api/client'
import type { DeclarationComment, DeclarationDocument, DeclarationState } from '../garage/api'

export type { DeclarationComment, DeclarationDocument, DeclarationState }

/** One row of O1's inbox (§5.2), with the garage contact the officer needs to chase it. */
export interface OfficerDeclarationListItem {
  id: string
  state: DeclarationState
  plateNo: string
  insuredName: string | null
  garageName: string | null
  garageEmail: string | null
  garagePhone: string | null
  createdAt: string
  submittedAt: string | null
  mediaCount: number
}

/** O2's review screen. The officer sees everything, including the garage's note and every comment. */
export interface OfficerDeclarationDetail {
  id: string
  state: DeclarationState
  plateNo: string
  insuredName: string | null
  note: string | null
  visaNo: string | null
  createdAt: string
  submittedAt: string | null
  decidedAt: string | null
  garageName: string | null
  garageEmail: string | null
  garagePhone: string | null
  comments: DeclarationComment[]
}

/** One NEXT3 search hit (§6.1's claim search). */
export interface ClaimSearchResult {
  visaNo: string
  plateNo: string
  insuredName: string
}

export const officerKeys = {
  all: ['officer', 'declarations'] as const,
  lists: () => [...officerKeys.all, 'list'] as const,
  list: (state?: string) => [...officerKeys.lists(), state ?? ''] as const,
  detail: (declarationId: string) => [...officerKeys.all, 'detail', declarationId] as const,
  documents: (declarationId: string) => [...officerKeys.all, 'documents', declarationId] as const,
}

export function officerDocumentsPath(declarationId: string): string {
  return `/api/officer/declarations/${declarationId}/documents`
}

/**
 * Where a document's bytes come from. Fetched through `apiBlob` and rendered as an object URL —
 * never used as an `<img src>` directly, because a browser sends no Authorization header for one and
 * the resulting 401 would sign the officer out mid-review.
 */
export function officerDocumentContentPath(declarationId: string, documentId: string): string {
  return `${officerDocumentsPath(declarationId)}/${documentId}/content`
}

export function listInbox(
  state?: string,
  signal?: AbortSignal,
): Promise<OfficerDeclarationListItem[]> {
  const query = state ? `?${new URLSearchParams({ state }).toString()}` : ''
  return api<OfficerDeclarationListItem[]>(`/api/officer/declarations${query}`, { signal })
}

export function getOfficerDeclaration(
  declarationId: string,
  signal?: AbortSignal,
): Promise<OfficerDeclarationDetail> {
  return api<OfficerDeclarationDetail>(`/api/officer/declarations/${declarationId}`, { signal })
}

export function listOfficerDocuments(
  declarationId: string,
  signal?: AbortSignal,
): Promise<DeclarationDocument[]> {
  return api<DeclarationDocument[]>(officerDocumentsPath(declarationId), { signal })
}

/**
 * §6.1's claim search — the officer's visa lookup, and `INext3Client.SearchClaims`' first caller in
 * the whole application. Slice 3.2 removed the expert's, because a NEXT3-wide search on a screen with
 * a capture panel on it is an invitation to attach photos to a stranger's claim. Here it is the point.
 */
export function searchClaims(
  plateNo: string,
  visaNo: string,
  signal?: AbortSignal,
): Promise<ClaimSearchResult[]> {
  const params = new URLSearchParams()
  if (plateNo) params.set('plateNo', plateNo)
  if (visaNo) params.set('visaNo', visaNo)
  return api<ClaimSearchResult[]>(`/api/officer/claims/search?${params.toString()}`, { signal })
}

export function approveDeclaration(
  declarationId: string,
  visaNo: string,
  comment: string,
): Promise<{ state: DeclarationState }> {
  return api<{ state: DeclarationState }>(
    `/api/officer/declarations/${declarationId}/approve`,
    { method: 'POST', body: JSON.stringify({ visaNo, comment }) },
  )
}

export function rejectDeclaration(
  declarationId: string,
  comment: string,
): Promise<{ state: DeclarationState }> {
  return api<{ state: DeclarationState }>(
    `/api/officer/declarations/${declarationId}/reject`,
    { method: 'POST', body: JSON.stringify({ comment }) },
  )
}

/**
 * The signed-in user, for the name that goes on #18's approval image.
 *
 * Re-exported rather than declared here since slice 4.4: `AppHeader` needs the same call for every
 * role, and `ui/` may not import a role module. One definition, one query key (`meKey`) — the header
 * and the approval image must not be able to disagree about who is signed in.
 */
export { getMe, meKey, type Me } from '../api/me'
