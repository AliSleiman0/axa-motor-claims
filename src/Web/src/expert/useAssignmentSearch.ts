import { useEffect, useState } from 'react'
import { useSearchParams } from 'react-router-dom'

/**
 * How long typing settles before it becomes a request. Long enough that a visa number typed at
 * speed is one round trip rather than fifteen, short enough not to feel broken.
 */
export const SEARCH_DEBOUNCE_MS = 300

/**
 * The server refuses a longer term with `search_term_too_long`. Mirrored here as an input
 * `maxLength` so the screen cannot produce a request the server will reject — the server is the
 * control, this is only the affordance, the same split as §7.2's clarity gate.
 */
export const SEARCH_MAX_LENGTH = 64

export interface AssignmentSearch {
  /** What is in the box right now — updates on every keystroke, so typing feels immediate. */
  term: string
  setTerm: (next: string) => void
  /** The settled term, taken from the URL. This is what the request and the query key use. */
  query: string
}

/**
 * E1's search box state (slice 3.2).
 *
 * **The URL is the source of truth**, not component state: `?q=` is what makes a reload — or a
 * shared link, or the browser restoring a tab after Android killed it under memory pressure — keep
 * the search the expert had. The local `term` exists only so the input does not lag behind the
 * debounce.
 *
 * `replace: true` because every settled keystroke would otherwise be a history entry, and Back
 * would walk the expert through their own typing one character at a time instead of leaving the
 * screen.
 */
export function useAssignmentSearch(debounceMs: number = SEARCH_DEBOUNCE_MS): AssignmentSearch {
  const [searchParams, setSearchParams] = useSearchParams()
  const query = searchParams.get('q') ?? ''
  const [term, setTerm] = useState(query)

  useEffect(() => {
    if (term === query) return

    const timer = setTimeout(() => {
      setSearchParams(
        (current) => {
          const next = new URLSearchParams(current)
          if (term) {
            next.set('q', term)
          } else {
            next.delete('q')
          }
          return next
        },
        { replace: true },
      )
    }, debounceMs)

    return () => {
      clearTimeout(timer)
    }
  }, [term, query, debounceMs, setSearchParams])

  return { term, setTerm, query }
}
