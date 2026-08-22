import { useEffect, useRef } from 'react'
import { Button } from './Button'

export interface ConfirmDialogProps {
  /** The whole question, as a sentence. Null closes the dialog. */
  message: string | null
  /** The destructive verb — "Deactivate". Never "OK": a button should name what it does. */
  confirmLabel: string
  onConfirm: () => void
  onCancel: () => void
}

/**
 * The one destructive action gets the one dialog (pass 3's `A1List`).
 *
 * A native `<dialog>` opened with `showModal()`, which is what makes this small: the element handles
 * the focus trap, the inert background and the Escape key itself, so none of that is hand-rolled
 * here — and hand-rolled focus traps are how a keyboard user ends up behind a modal.
 *
 * **It replaces `window.confirm`, which was untestable and unstylable** — the old version could only
 * be checked by stubbing a global and asserting the stub, which is a test of the double rather than
 * of the screen.
 *
 * **The dialog is opened imperatively and never by an `open` attribute, and that distinction cost a
 * blank screen.** The first version rendered `<dialog open>` so that jsdom — which has no
 * `showModal` — would still show it to a test. In a real browser `open` puts the element into the
 * *non-modal* state, `showModal()` then throws `InvalidStateError`, and because that happens inside
 * an effect it takes the whole React tree down: pressing Deactivate blanked the page. Every test
 * passed. Found in the 4.4 browser pass; the fallback below is now the jsdom branch only, so Chrome
 * takes the modal path it is supposed to.
 */
export function ConfirmDialog({ message, confirmLabel, onConfirm, onCancel }: ConfirmDialogProps) {
  const dialog = useRef<HTMLDialogElement | null>(null)

  useEffect(() => {
    const element = dialog.current
    if (!element || !message) return

    if (typeof element.showModal === 'function') {
      element.showModal()
    } else {
      // jsdom only. Never both: `open` makes the element non-modal, and `showModal` then throws.
      element.open = true
    }

    return () => {
      if (typeof element.close === 'function') element.close()
      else element.open = false
    }
  }, [message])

  if (!message) return null

  return (
    <dialog
      ref={dialog}
      className="confirm"
      aria-label={message}
      // Escape fires `cancel` rather than a click, and without this the dialog would close while the
      // caller still believed it open.
      onCancel={(event) => {
        event.preventDefault()
        onCancel()
      }}
    >
      <p className="confirm__message">{message}</p>
      <div className="actions">
        <Button onClick={onCancel}>Cancel</Button>
        <Button variant="destructive" onClick={onConfirm}>
          {confirmLabel}
        </Button>
      </div>
    </dialog>
  )
}
