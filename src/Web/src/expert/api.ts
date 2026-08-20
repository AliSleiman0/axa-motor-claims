import { api } from '../api/client'
import type { Position } from './geolocation'

/**
 * One row of E1. The claim fields are null when the NEXT3 cache is cold — an assignment that arrived
 * while NEXT3 was down is still a real assignment (design.md §4, §5.1).
 */
export interface AssignmentListItem {
  id: string
  visaNo: string
  receivedAt: string
  openedAt: string | null
  arrivedAt: string | null
  plateNo: string | null
  insuredName: string | null
  carMakeModel: string | null
  accidentDate: string | null
  mediaCount: number
}

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

/** E2. `claimStatus` is 'fresh' | 'stale' | 'not_found'; stale drives §4's staleness banner. */
export interface AssignmentDetail {
  id: string
  visaNo: string
  receivedAt: string
  openedAt: string | null
  arrivedAt: string | null
  claimStatus: 'fresh' | 'stale' | 'not_found'
  claimFetchedAt: string | null
  claim: Claim | null
}

export interface Arrival {
  arrivedAt: string
  latitude: number | null
  longitude: number | null
}

/** A document as E3 lists it — the API's `DocumentDto` (slice 2.3). */
export interface AssignmentDocument {
  id: string
  bucket: string
  docType: string | null
  origin: 'captured' | 'uploaded'
  clarityResult: string
  contentType: string
  sizeBytes: number
  pushStatus: string
  blobRetained: boolean
  createdAt: string
}

export const expertKeys = {
  all: ['expert', 'assignments'] as const,
  list: () => [...expertKeys.all, 'list'] as const,
  detail: (assignmentId: string) => [...expertKeys.all, 'detail', assignmentId] as const,
  documents: (assignmentId: string) => [...expertKeys.all, 'documents', assignmentId] as const,
}

/** E3's upload target (slice 2.3). Also the path the capture component posts to. */
export function documentsPath(assignmentId: string): string {
  return `/api/expert/assignments/${assignmentId}/documents`
}

export function listDocuments(
  assignmentId: string,
  signal?: AbortSignal,
): Promise<AssignmentDocument[]> {
  return api<AssignmentDocument[]>(documentsPath(assignmentId), { signal })
}

export function listAssignments(signal?: AbortSignal): Promise<AssignmentListItem[]> {
  return api<AssignmentListItem[]>('/api/expert/assignments', { signal })
}

export function getAssignment(assignmentId: string, signal?: AbortSignal): Promise<AssignmentDetail> {
  return api<AssignmentDetail>(`/api/expert/assignments/${assignmentId}`, { signal })
}

/** Coordinates only: the arrival date and time are the server's clock, not the handset's (§5.1). */
export function postArrival(assignmentId: string, position: Position): Promise<Arrival> {
  return api<Arrival>(`/api/expert/assignments/${assignmentId}/arrival`, {
    method: 'POST',
    body: JSON.stringify({ latitude: position.latitude, longitude: position.longitude }),
  })
}
