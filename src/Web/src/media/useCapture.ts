import { useEffect, useRef, useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { CLARITY_EXPLANATIONS, assessClarity, type ClarityVerdict } from './clarity'
import { DECODE_EXPLANATIONS, DecodeError, decodeImage, type ImageDecoder } from './decode'
import { findBucket, type MediaConfig } from './config'
import { describeUploadError, uploadDocument, type MediaOrigin } from './upload'

export type CaptureStage = 'idle' | 'assessing' | 'confirm' | 'rejected' | 'uploading'

export interface CaptureCandidate {
  file: File
  origin: MediaOrigin
  previewUrl: string
  /** Null for a PDF: §7.2's checks are dimensions and blur, neither of which a PDF has. */
  verdict: ClarityVerdict | null
}

export interface UseCaptureOptions {
  /** The upload endpoint. A parameter so garage (5.1) and the public page (5.3) reuse this hook. */
  path: string
  bucket: string
  config: MediaConfig | undefined
  /** Called after a successful upload — the caller owns cache invalidation, not this module. */
  onUploaded?: () => void
  decode?: ImageDecoder
  makePreviewUrl?: (blob: Blob) => string
  revokePreviewUrl?: (url: string) => void
}

export interface UseCaptureResult {
  stage: CaptureStage
  /** §7.1: false for the car-photo buckets, and the reason no file picker is rendered for them. */
  allowUpload: boolean
  /** For the input's `accept` attribute — the bucket's own list, so nothing is offered that the server refuses. */
  acceptTypes: string
  candidate: CaptureCandidate | null
  /** One message at a time: a clarity refusal, a decode failure, or the server's rejection. */
  problem: string | null
  /** The thresholds and bucket rules have arrived. False only while `GET /api/config/media` is in flight. */
  configLoaded: boolean
  /**
   * This bucket exists in §7.1's registry, so capture may be offered. Deliberately separate from
   * `configLoaded`: collapsing the two renders a permanent "loading" for a bucket the server has
   * never heard of, which is the shape slice 5.1 will hit the moment a garage bucket is added to a
   * screen before its `MediaBuckets` entry and migration.
   */
  ready: boolean
  select: (file: File, origin: MediaOrigin) => void
  confirm: () => void
  retake: () => void
}

/**
 * design.md §7.2's gate, as a hook with no knowledge of who is capturing.
 *
 * This is the component slice 5.1 (garage) and 5.3/6.1 (the Option 2 public page) must reuse
 * unchanged — that reuse is part of what design.md §11 spent to keep Option 2 inside 8 weeks. So it
 * imports nothing from `../expert`, takes its endpoint as a parameter, and hands cache invalidation
 * back to the caller through `onUploaded`. A test asserts the absence of that coupling, because it
 * is the kind of thing that creeps back in one convenient import at a time.
 *
 * The flow is §7.2's, in order: pick or capture -> resolution floor -> blur variance -> the E4
 * confirm screen -> upload. The gate is user experience, not a control; the server re-checks
 * everything (§7.2 item 5), which is why a client bypass still fails.
 */
export function useCapture({
  path,
  bucket,
  config,
  onUploaded,
  decode,
  makePreviewUrl = URL.createObjectURL,
  revokePreviewUrl = URL.revokeObjectURL,
}: UseCaptureOptions): UseCaptureResult {
  const [stage, setStage] = useState<CaptureStage>('idle')
  const [candidate, setCandidate] = useState<CaptureCandidate | null>(null)
  const [problem, setProblem] = useState<string | null>(null)

  const rule = findBucket(config, bucket)
  const allowUpload = rule?.allowUpload ?? false

  // A latch rather than `mutation.isPending`: two taps in the same tick both run before React
  // re-renders, so state has not caught up and the second would send a second upload — a duplicate
  // photo under the visa, and a second outbox row. Slice 2.4 verified this exact swap turns one
  // request into two.
  const inFlight = useRef(false)
  // Guards against a slow decode resolving after the expert has already retaken the shot: only the
  // newest selection may write state.
  const generation = useRef(0)
  const previewUrl = useRef<string | null>(null)
  const revoke = useRef(revokePreviewUrl)

  useEffect(() => {
    revoke.current = revokePreviewUrl
  })

  // Object URLs pin their blob until revoked, and E3 is a screen an expert stays on while filling
  // four buckets.
  useEffect(
    () => () => {
      if (previewUrl.current) revoke.current(previewUrl.current)
    },
    [],
  )

  function releasePreview() {
    if (previewUrl.current) {
      revoke.current(previewUrl.current)
      previewUrl.current = null
    }
  }

  const mutation = useMutation({
    mutationFn: (accepted: CaptureCandidate) =>
      uploadDocument({ path, bucket, origin: accepted.origin, file: accepted.file }),
  })

  function reset() {
    generation.current += 1
    releasePreview()
    setCandidate(null)
    setStage('idle')
  }

  function select(file: File, origin: MediaOrigin) {
    // §7.1's capture-only rule, applied before anything else happens. The server refuses this too
    // (`upload_not_allowed_for_bucket`), but a UI that offers the choice and then fails it is a UI
    // that taught the expert the wrong thing.
    if (origin === 'uploaded' && !allowUpload) {
      setProblem(
        'Car photos must be taken with the camera now. This is the whole point of the app: AXA ' +
          'needs the pictures taken at the scene.',
      )
      setStage('rejected')
      return
    }

    generation.current += 1
    const mine = generation.current
    releasePreview()
    setProblem(null)

    const url = makePreviewUrl(file)
    previewUrl.current = url
    const base = { file, origin, previewUrl: url }

    // §7.2 measures dimensions and blur; a PDF has neither, and the server stores
    // `clarity_result = not_applicable` for it. Straight to the confirm screen.
    if (!file.type.startsWith('image/')) {
      setCandidate({ ...base, verdict: null })
      setStage('confirm')
      return
    }

    if (!config) {
      setProblem('Photo quality settings have not loaded yet. Try again in a moment.')
      setStage('rejected')
      return
    }

    setCandidate({ ...base, verdict: null })
    setStage('assessing')

    const thresholds = config.clarity
    decodeImage(file, thresholds.blurAnalysisMaxEdge, decode)
      .then((decoded) => {
        if (generation.current !== mine) return
        const verdict = assessClarity(decoded, decoded.analysis, thresholds)
        setCandidate({ ...base, verdict })
        if (verdict.ok) {
          setStage('confirm')
        } else {
          setProblem(CLARITY_EXPLANATIONS[verdict.failure ?? 'too_blurry'])
          setStage('rejected')
        }
      })
      .catch((error: unknown) => {
        if (generation.current !== mine) return
        setProblem(
          error instanceof DecodeError
            ? DECODE_EXPLANATIONS[error.reason]
            : 'This photo could not be checked. Take it again.',
        )
        setStage('rejected')
      })
  }

  function confirm() {
    if (!candidate || inFlight.current || stage !== 'confirm') return
    inFlight.current = true
    setProblem(null)
    setStage('uploading')

    mutation.mutate(candidate, {
      onSuccess: () => {
        inFlight.current = false
        reset()
        onUploaded?.()
      },
      onError: (error) => {
        // Released on failure only, so the expert can press Confirm again on the same shot.
        inFlight.current = false
        setProblem(describeUploadError(error))
        setStage('confirm')
      },
    })
  }

  function retake() {
    setProblem(null)
    reset()
  }

  return {
    stage,
    allowUpload,
    acceptTypes: rule?.contentTypes.join(',') ?? '',
    candidate,
    problem,
    configLoaded: config !== undefined,
    ready: rule !== undefined,
    select,
    confirm,
    retake,
  }
}
