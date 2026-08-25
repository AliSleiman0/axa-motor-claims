import { useEffect, useMemo } from 'react'
import { useQuery } from '@tanstack/react-query'
import { apiBlob } from '../api/client'

export interface UseDocumentBlobUrlOptions {
  /** False keeps the bytes unfetched — a PDF row asks for them only when the officer clicks. */
  enabled: boolean
  makeUrl?: (blob: Blob) => string
  revokeUrl?: (url: string) => void
}

export interface UseDocumentBlobUrlResult {
  url: string | null
  pending: boolean
  failed: boolean
}

/**
 * One document's bytes as a `blob:` URL (slice 4.2).
 *
 * **This exists because a browser cannot authenticate an `<img src>`.** The session is a bearer token
 * added as a header by `withBearer`; an `<img>` or a plain `<a href>` aimed at `/api/…/content` sends
 * no header, comes back 401, and `api()`'s 401 branch hard-navigates to `/login` — so an officer would
 * be signed out by looking at a photograph. The fetch goes through `apiBlob`, which shares that
 * bearer and the single refresh-and-retry; only the rendered `src` is a `blob:` URL.
 *
 * The fetch is a `useQuery` rather than a hand-rolled effect: it is server state like every other
 * fetch in the app, so it caches across a remount and reports its own loading and error states
 * without a `setState` in an effect body — which the React lint rules reject outright, and rightly,
 * since it is a cascading render per document on a screen that lists a dozen.
 *
 * `makeUrl` / `revokeUrl` are injectable for the reason every browser seam in `media/` is: jsdom
 * implements neither, and the house idiom is a defaulted parameter rather than a module mock.
 */
export function useDocumentBlobUrl(
  path: string,
  {
    enabled,
    makeUrl = URL.createObjectURL,
    revokeUrl = URL.revokeObjectURL,
  }: UseDocumentBlobUrlOptions,
): UseDocumentBlobUrlResult {
  const query = useQuery({
    queryKey: ['officer', 'document-content', path],
    queryFn: () => apiBlob(path),
    enabled,
    // The bytes cannot change under a given document id — §7.3 deletes them, it never rewrites them
    // — so refetching one is pure cost on a screen an officer sits on.
    staleTime: Number.POSITIVE_INFINITY,
  })

  const blob = query.data
  const url = useMemo(() => (blob ? makeUrl(blob) : null), [blob, makeUrl])

  // Object URLs pin their blob until revoked, and O2 is a screen an officer stays on while reading a
  // dozen photographs — so this is the difference between a review session and a tab that grows by
  // 15 MB a claim. Keyed on the url itself, so a replaced one is released as well as an unmounted one.
  useEffect(() => {
    if (!url) return undefined
    return () => {
      revokeUrl(url)
    }
  }, [url, revokeUrl])

  return {
    url,
    pending: enabled && query.isPending,
    // A 404 here is a real and expected state: §7.3 sweeps the bytes once NEXT3 has them. The row
    // survives, so the screen can still say what the document was — it just cannot show it.
    failed: query.isError,
  }
}
