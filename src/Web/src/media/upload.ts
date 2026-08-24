import { ApiError, api } from '../api/client'

/** design.md §4's provenance flag, kept on every document row and required by Broker Option 1. */
export type MediaOrigin = 'captured' | 'uploaded'

export interface UploadRequest {
  /**
   * The endpoint to post to. A parameter, not a constant, because this module is the one slice 5.1's
   * garage flow and 5.3's public page reuse — they post the same body to their own owner's route.
   */
  path: string
  bucket: string
  origin: MediaOrigin
  file: File
  /**
   * Which client sends it (slice 5.3). Defaults to `'bearer'`, so every caller written before this
   * option existed behaves exactly as it did.
   *
   * `'none'` is for §5.3's public page, and it is not merely an optimisation. `api()` attaches an
   * `Authorization` header whenever `localStorage` happens to hold tokens — which it does whenever a
   * broker opens their own customer's link to check it — and its 401 branch **hard-navigates to
   * `/login`**. A member of the public bounced to a staff sign-in screen is the worst failure this
   * page has, and it would only ever reproduce on a machine that had signed in. So the public path
   * skips `send()` entirely rather than relying on "there are no tokens" staying true.
   */
  auth?: 'bearer' | 'none'
}

/** The server's 201 body (`DocumentDto`). */
export interface UploadedDocument {
  id: string
  bucket: string
  docType: string | null
  origin: MediaOrigin
  clarityResult: string
  contentType: string
  /** The stored file name (slice 4.1). Null for a row written before that column existed. */
  fileName: string | null
  sizeBytes: number
  pushStatus: string
  pushConfirmed: boolean
  blobRetained: boolean
  createdAt: string
}

/**
 * Builds the multipart body the streamed endpoint requires.
 *
 * **The order is the contract.** The upload is streamed and never buffered, so the server has to
 * know the bucket before the bytes reach storage — a capture-only rule cannot be applied to a file
 * that has already been written. Metadata parts first, file last; out of order is
 * `400 metadata_must_precede_file`. `FormData` preserves insertion order, so this function's
 * statement order *is* the wire order.
 */
export function buildUploadBody(bucket: string, origin: MediaOrigin, file: File): FormData {
  const body = new FormData()
  body.append('bucket', bucket)
  body.append('origin', origin)
  body.append('file', file, file.name)
  return body
}

export async function uploadDocument({
  path,
  bucket,
  origin,
  file,
  auth = 'bearer',
}: UploadRequest): Promise<UploadedDocument> {
  const body = buildUploadBody(bucket, origin, file)

  if (auth === 'bearer') {
    return api<UploadedDocument>(path, { method: 'POST', body })
  }

  // A plain fetch: no bearer, no refresh-and-retry, no redirect. `ApiError` is still what a refusal
  // throws, so `describeUploadError` and every caller's error branch behave identically either way —
  // the difference is confined to who is asking, which is the only thing that differs.
  const response = await fetch(path, { method: 'POST', body })
  const text = await response.text()
  if (!response.ok) throw new ApiError(response.status, text)
  return JSON.parse(text) as UploadedDocument
}

/**
 * The server's rejection codes (slice 2.3), turned into something an expert at a roadside can act
 * on. Raw codes are for the log; a person needs to know whether to retake the photo, use a
 * different file, or stop trying.
 */
const UPLOAD_EXPLANATIONS: Record<string, string> = {
  image_too_small:
    'AXA refused this photo as too low-resolution. Take it again with the rear camera at full ' +
    'quality.',
  file_too_large: 'This file is too large to send. Take the photo again at a normal quality setting.',
  content_type_not_allowed: 'That file type cannot be sent to this section. Use a photo or a PDF.',
  content_type_mismatch:
    'That file is not the type its name claims. Take the photo again rather than attaching a ' +
    'renamed file.',
  upload_not_allowed_for_bucket:
    'Car photos must be taken with the camera now, not chosen from the gallery.',
  unreadable_image: 'This photo could not be read. Take it again.',
  file_empty: 'That file is empty. Take the photo again.',
  file_missing: 'No photo was attached. Take the photo again.',
  // Slice 4.1's two refusals. Without these a garage that tried to attach the officer's approval
  // image, or to add a document after AXA had already decided, would read a bare "(400)"/"(409)".
  bucket_not_allowed_for_caller:
    'That kind of file cannot be added here. Use the sections on this screen.',
  declaration_already_decided:
    'AXA has already decided this declaration, so nothing more can be attached to it.',
  // Slice 5.1's two. The first is the repair buckets' half of the same rule — the same file is
  // welcome once the repair has started and refused before and after it. The second is the race:
  // the declaration moved on *while* this file was uploading, so the answer is to look, not to
  // conclude that nothing more can ever be attached.
  repairs_not_in_progress:
    'Repair documents can only be added once the repair has been started, and before it is finished.',
  declaration_changed:
    'This declaration moved on while the file was uploading. Reopen it to see where it is now.',
  // Slice 5.3's two, for §9.1's caps. Added in the same commit as the server that returns them —
  // 4.2's note (7) and 5.1's note (8) were both this omission, a bare "(400)" for a week.
  too_many_files:
    'That is as many files as this form accepts. Remove one before adding another.',
  request_already_submitted:
    'This has already been sent, so nothing more can be added to it.',
}

/** Pulls the `{ "error": "code" }` body the media endpoint returns on a refusal. */
export function uploadErrorCode(error: unknown): string | null {
  if (!(error instanceof ApiError)) return null
  try {
    const parsed: unknown = JSON.parse(error.body)
    if (typeof parsed === 'object' && parsed !== null && 'error' in parsed) {
      const code = (parsed as { error: unknown }).error
      return typeof code === 'string' ? code : null
    }
  } catch {
    // A non-JSON body (a proxy error page, say) is not a code — fall through to the generic text.
  }
  return null
}

export function describeUploadError(error: unknown): string {
  const code = uploadErrorCode(error)
  if (code && code in UPLOAD_EXPLANATIONS) return UPLOAD_EXPLANATIONS[code]
  if (error instanceof ApiError) {
    return `This photo was not sent (${error.status}). Check the connection and try again.`
  }
  return 'This photo was not sent. Check the connection and try again.'
}
