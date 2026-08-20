import { useRef, useState } from 'react'
import { ClarityConfirm } from '../ClarityConfirm'
import type { MediaConfig } from '../config'
import { shortId } from '../recorder'
import { useCapture } from '../useCapture'
import { CarDiagram } from './car'
import { clearMarks, markLabels, toggleMark, type Marks } from './marks'
import {
  DIAGRAM_EXPLANATIONS,
  DIAGRAM_RENDER,
  DiagramRenderError,
  diagramFitsFloor,
  renderDiagramToFile,
  serializeDiagram,
  type DiagramRenderer,
} from './render'

interface DiagramPanelProps {
  path: string
  bucket: string
  label: string
  config: MediaConfig | undefined
  count?: number
  onUploaded?: () => void
  /** Injected by tests; production takes the canvas renderer. */
  render?: DiagramRenderer
  makeId?: () => string
}

/**
 * design.md §1's car body diagram: static SVG, tap-to-mark, flattened to PNG (§5.1's row).
 *
 * **Functional, not polished** — §11 spent the polish to fund Broker Option 2, and that trade is
 * only honoured by not quietly re-spending it here.
 *
 * The PNG goes through the §7 pipeline like everything else, but `photographic: false`: it is an
 * image, so the server applies §7.2's resolution floor to it, while the client's blur pass would be
 * measuring the focus of a drawing.
 */
export function DiagramPanel({
  path,
  bucket,
  label,
  config,
  count,
  onUploaded,
  render,
  makeId = () => shortId(),
}: DiagramPanelProps) {
  const capture = useCapture({ path, bucket, config, onUploaded })
  const [marks, setMarks] = useState<Marks>(clearMarks())
  const [problem, setProblem] = useState<string | null>(null)
  const svg = useRef<SVGSVGElement | null>(null)
  // 1.5's lesson again: two taps in the same tick both pass a state check, and the second would
  // render and select a duplicate diagram.
  const rendering = useRef(false)

  const fitsFloor = diagramFitsFloor(config)

  function use() {
    if (rendering.current || !svg.current) return
    rendering.current = true
    setProblem(null)

    renderDiagramToFile(serializeDiagram(svg.current), makeId(), render)
      .then((file) => {
        setMarks(clearMarks())
        capture.select(file, 'captured', { photographic: false })
      })
      .catch((error: unknown) => {
        setProblem(
          error instanceof DiagramRenderError
            ? DIAGRAM_EXPLANATIONS[error.reason]
            : DIAGRAM_EXPLANATIONS.failed,
        )
      })
      .finally(() => {
        rendering.current = false
      })
  }

  const busy = capture.stage === 'uploading'
  const marked = markLabels(marks)

  return (
    <section>
      <h4>
        {label}
        {typeof count === 'number' ? ` (${count})` : ''}
      </h4>

      {!capture.configLoaded ? (
        <p>Loading diagram settings…</p>
      ) : !capture.ready ? (
        <p role="alert">This section is not configured for uploads yet, so nothing can be sent to it.</p>
      ) : !fitsFloor ? (
        // The loud half of the floor guard. Silently letting the expert draw a diagram the server
        // will refuse as `image_too_small` would blame the drawing for a configuration change; the
        // other half is `TheClarityFloorAdmitsADamageDiagram`, which fails the build instead.
        <p role="alert">
          Diagrams cannot be sent: AXA now requires at least {config?.clarity.minWidth}×
          {config?.clarity.minHeight}, and this diagram is drawn at {DIAGRAM_RENDER.width}×
          {DIAGRAM_RENDER.height}. Report this — it is a settings problem, not something you can fix
          here.
        </p>
      ) : capture.candidate && (capture.stage === 'confirm' || capture.stage === 'uploading') ? (
        <ClarityConfirm
          previewUrl={capture.candidate.previewUrl}
          file={capture.candidate.file}
          verdict={capture.candidate.verdict}
          pending={busy}
          onConfirm={capture.confirm}
          onRetake={capture.retake}
        />
      ) : (
        <div>
          <CarDiagram
            marks={marks}
            svgRef={svg}
            onToggle={(id) => setMarks((current) => toggleMark(current, id))}
          />
          <p>{marked.length > 0 ? `Marked: ${marked.join(', ')}` : 'Tap the damaged panels.'}</p>
          <button type="button" onClick={() => setMarks(clearMarks())} disabled={marks.length === 0}>
            Clear
          </button>{' '}
          <button type="button" onClick={use}>
            Use this diagram
          </button>
        </div>
      )}

      {problem ? <p role="alert">{problem}</p> : null}
      {capture.problem ? <p role="alert">{capture.problem}</p> : null}
    </section>
  )
}
