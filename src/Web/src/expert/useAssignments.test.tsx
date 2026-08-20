import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { renderHook } from '@testing-library/react'
import type { ReactNode } from 'react'
import { describe, expect, it } from 'vitest'
import { expertKeys } from './api'
import { useRefreshAfterCapture } from './useAssignments'

const ASSIGNMENT_ID = '00000000-0000-0000-0000-00000000a001'

describe('the expert query keys are the invalidation contract', () => {
  it('nests every list key under the lists() prefix', () => {
    // The property everything below depends on. `invalidateQueries` matches by prefix, so this is
    // what makes one invalidation reach a filtered list and an unfiltered one alike.
    expect(expertKeys.list('PLC-TEST-T2').slice(0, expertKeys.lists().length)).toEqual([
      ...expertKeys.lists(),
    ])
    expect(expertKeys.list()).toEqual([...expertKeys.lists(), ''])
  })

  it('refreshes the list the expert is looking at, even with a search term active', () => {
    // The trap the playbook card names. E1 shows a media count per assignment (§5.1), so an upload
    // has to invalidate the list as well as the documents — and after slice 3.2 the *active* list
    // is keyed by the search term. Invalidate `list()` instead of `lists()` here and this goes red
    // while every other expert test stays green: the count silently stops updating for any expert
    // who searched first.
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const searched = expertKeys.list('PLC-TEST-T2')
    queryClient.setQueryData(searched, [])
    queryClient.setQueryData(expertKeys.documents(ASSIGNMENT_ID), [])

    const wrapper = ({ children }: { children: ReactNode }) => (
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    )
    const { result } = renderHook(() => useRefreshAfterCapture(ASSIGNMENT_ID), { wrapper })
    result.current()

    expect(queryClient.getQueryState(searched)?.isInvalidated).toBe(true)
    expect(queryClient.getQueryState(expertKeys.documents(ASSIGNMENT_ID))?.isInvalidated).toBe(true)
  })

  it('also refreshes an unfiltered list', () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    queryClient.setQueryData(expertKeys.list(), [])

    const wrapper = ({ children }: { children: ReactNode }) => (
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    )
    const { result } = renderHook(() => useRefreshAfterCapture(ASSIGNMENT_ID), { wrapper })
    result.current()

    expect(queryClient.getQueryState(expertKeys.list())?.isInvalidated).toBe(true)
  })
})
