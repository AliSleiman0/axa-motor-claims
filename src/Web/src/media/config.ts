import { api } from '../api/client'
import type { ClarityThresholds } from './clarity'

/** One row of design.md §7.1's matrix, as `GET /api/config/media` serves it. */
export interface BucketConfig {
  bucket: string
  /** §7.1's capture-only rule. False for the car-photo buckets — no file picker may be offered. */
  allowUpload: boolean
  contentTypes: string[]
}

export interface MediaConfig {
  clarity: ClarityThresholds
  maxFileMb: number
  buckets: BucketConfig[]
}

export const mediaKeys = {
  all: ['media'] as const,
  config: () => [...mediaKeys.all, 'config'] as const,
}

/**
 * The thresholds and bucket rules come from the server, never from constants here.
 *
 * CLAUDE.md's placeholder rule names *thresholds* explicitly: a client-specific value lives only in
 * `appsettings.Placeholders.json`, so a literal 1024 in this codebase would be a bug and a second
 * source of truth that drifts from the one the server rejects uploads with. The endpoint is
 * anonymous so slice 5.3's public page can call it with no token.
 */
export function fetchMediaConfig(signal?: AbortSignal): Promise<MediaConfig> {
  return api<MediaConfig>('/api/config/media', { signal })
}

export function findBucket(config: MediaConfig | undefined, bucket: string): BucketConfig | undefined {
  return config?.buckets.find((candidate) => candidate.bucket === bucket)
}
