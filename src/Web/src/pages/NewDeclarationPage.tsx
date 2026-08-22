import { useRef, useState, type FormEvent } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate } from 'react-router-dom'
import { ApiError } from '../api/client'
import { createDeclaration, garageKeys } from '../garage/api'
import { AlertBanner } from '../ui/Banner'
import { Button } from '../ui/Button'
import { TextArea, TextField } from '../ui/fields'

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
    <section className="page">
      <Link className="back-link" to="/garage">
        ← My declarations
      </Link>
      <h2 className="page__title">New declaration</h2>

      <form className="panel" onSubmit={onSubmit}>
        <TextField
          id="plate-no"
          label="Plate number"
          value={plateNo}
          maxLength={20}
          required
          // The plate is the key an officer searches NEXT3 with, so it reads as a reference number.
          className="field__control--mono"
          onChange={(event) => {
            setPlateNo(event.target.value)
          }}
        />
        <TextField
          id="insured-name"
          label="Insured name (optional)"
          value={insuredName}
          maxLength={200}
          onChange={(event) => {
            setInsuredName(event.target.value)
          }}
        />
        <TextArea
          id="declaration-note"
          label="Note (optional)"
          value={note}
          maxLength={1000}
          onChange={(event) => {
            setNote(event.target.value)
          }}
        />
        <div className="actions">
          <Button
            variant="primary"
            type="submit"
            disabled={mutation.isPending || plateNo.trim().length === 0}
          >
            {mutation.isPending ? 'Creating…' : 'Create declaration'}
          </Button>
        </div>
      </form>

      {failed && <AlertBanner>{failed}</AlertBanner>}
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
