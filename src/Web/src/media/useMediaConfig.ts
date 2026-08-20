import { useQuery } from '@tanstack/react-query'
import { fetchMediaConfig, mediaKeys } from './config'

/**
 * §7.2's thresholds and §7.1's bucket rules, fetched once and held.
 *
 * `staleTime: Infinity` because this is deployment configuration, not claim data: refetching it on
 * every screen would spend an expert's connection — the scarce resource in this application — on a
 * value that changes when someone edits a config file. A page reload picks up an edit.
 */
export function useMediaConfig() {
  return useQuery({
    queryKey: mediaKeys.config(),
    queryFn: ({ signal }) => fetchMediaConfig(signal),
    staleTime: Number.POSITIVE_INFINITY,
  })
}
