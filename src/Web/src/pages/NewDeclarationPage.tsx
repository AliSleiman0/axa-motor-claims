import { useRef, useState, type FormEvent } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate } from 'react-router-dom'
import { ApiError } from '../api/client'
import { createDeclaration, garageKeys } from '../garage/api'

/**
 * G2 (design.md §5.2): a new declaration in Draft.
 *
 * The form is §1's interpretation of a form the BRD never defines — **plate required**, because it is
 * the key the officer searches NEXT3 with and a declaration without one can never be linked to a
 * visa; insured name and note optional; nothing else. The `required` attribute and the check below
 * are the affordance, and the server is the control (`400 plate_required`).
 */
export default function NewDeclarationPage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [plateNo, setPlateNo] = useState('')
  const [insuredName, setInsuredName] = useState('')
  const [note, setNote] = useState('')
  const [failed, setFailed] = useState<string | null>(null)
  // The house latch: a double-submit here is two declarations for one car, and the garage would have
  // to notice and abandon one.
  const inFlight = useRef(false)

  const mutation = useMutation({
    mutationFn: () =>
      createDeclaration({
        plateNo: plateNo.trim(),
        insuredName: insuredName.trim() || null,
        note: note.trim() || null,
      }),
  })

  function onSubmit(event: FormEvent) {
    event.preventDefault()
    if (inFlight.current || plateNo.trim().length === 0) return

    inFlight.current = true
    setFailed(null)

    mutation.mutate(undefined, {
      onSuccess: async (created) => {
        await queryClient.invalidateQueries({ queryKey: garageKeys.lists() })
        // Straight to G3 in `draft`, which is where the documents go on. Landing back on the
        // worklist would make the garage find the row they just created before they could do the
        // next thing, which is the whole of the next thing.
        navigate(`/garage/${created.id}`)
      },
      onError: (error) => {
        inFlight.current = false
        setFailed(describe(error))
      },
    })
  }

  return (
    <section>
      <p>
        <Link to="/garage">← My declarations</Link>
      </p>
      <h2>New declaration</h2>

      <form onSubmit={onSubmit}>
        <p>
          <label htmlFor="plate-no">Plate number</label>{' '}
          <input
            id="plate-no"
            value={plateNo}
            maxLength={20}
            required
            onChange={(event) => {
              setPlateNo(event.target.value)
            }}
          />
        </p>
        <p>
          <label htmlFor="insured-name">Insured name (optional)</label>{' '}
          <input
            id="insured-name"
            value={insuredName}
            maxLength={200}
            onChange={(event) => {
              setInsuredName(event.target.value)
            }}
          />
        </p>
        <p>
          <label htmlFor="declaration-note">Note (optional)</label>{' '}
          <textarea
            id="declaration-note"
            value={note}
            maxLength={1000}
            onChange={(event) => {
              setNote(event.target.value)
            }}
          />
        </p>
        <button type="submit" disabled={mutation.isPending || plateNo.trim().length === 0}>
          {mutation.isPending ? 'Creating…' : 'Create declaration'}
        </button>
      </form>

      {failed && <p role="alert">{failed}</p>}
    </section>
  )
}

function describe(error: unknown): string {
  if (error instanceof ApiError && error.status === 400) {
    return 'A plate number is needed before this declaration can be created.'
  }

  return `The declaration was not created${
    error instanceof ApiError ? ` (${error.status})` : ''
  }. Check the connection and try again.`
}
