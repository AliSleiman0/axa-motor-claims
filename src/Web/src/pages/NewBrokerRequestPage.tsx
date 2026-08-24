import { useRef, useState, type FormEvent } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Link, useNavigate } from 'react-router-dom'
import { ApiError } from '../api/client'
import { brokerKeys, createRequest } from '../broker/api'
import { useBrokerConfig } from '../broker/useBroker'
import { AlertBanner } from '../ui/Banner'
import { Button } from '../ui/Button'
import { DateField, MoneyField, SelectField, TextArea, TextField } from '../ui/fields'

/**
 * B2's first half (design.md §5.3) — Option 1's six fields, saved as a `draft`.
 *
 * **Two screens rather than one**, mirroring G2 → G3, and for a structural reason rather than a
 * layout preference: a document is attached to a request, so the request has to exist before the
 * capture panel can post anything. There is no edit endpoint either, which makes the split honest —
 * the details go in once and a mistake means a new request, the same rule pass 3 already applies to
 * B4 ("a wrong submission means a new link").
 *
 * The insurance types come from the server (#14). A list in this file would be client data outside
 * the placeholder config, and a second source of truth from the one the server validates against.
 */
export default function NewBrokerRequestPage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const config = useBrokerConfig()

  const [insuredName, setInsuredName] = useState('')
  const [insuranceType, setInsuranceType] = useState('')
  const [insuredAddress, setInsuredAddress] = useState('')
  const [carValue, setCarValue] = useState('')
  const [estimatedPremium, setEstimatedPremium] = useState('')
  const [effectiveDate, setEffectiveDate] = useState('')
  const [failed, setFailed] = useState<string | null>(null)
  const inFlight = useRef(false)

  // All six, as the artboard's caption says out loud. The server refuses an incomplete request
  // (`400 incomplete_request`) — this is the affordance, not the control.
  const complete =
    insuredName.trim().length > 0 &&
    insuranceType.length > 0 &&
    insuredAddress.trim().length > 0 &&
    Number(carValue) > 0 &&
    Number(estimatedPremium) > 0 &&
    effectiveDate.length > 0

  const mutation = useMutation({
    mutationFn: () =>
      createRequest({
        insuredName: insuredName.trim(),
        insuranceType,
        insuredAddress: insuredAddress.trim(),
        carValue: Number(carValue),
        estimatedPremium: Number(estimatedPremium),
        effectiveDate,
      }),
  })

  function onSubmit(event: FormEvent) {
    event.preventDefault()
    if (inFlight.current || !complete) return

    inFlight.current = true
    setFailed(null)

    mutation.mutate(undefined, {
      onSuccess: async (created) => {
        await queryClient.invalidateQueries({ queryKey: brokerKeys.lists() })
        navigate(`/broker/${created.id}`)
      },
      onError: (error) => {
        inFlight.current = false
        setFailed(describe(error))
      },
    })
  }

  return (
    <section className="page">
      <Link to="/broker">← Requests</Link>
      <h2 className="page__title">New request</h2>
      <p className="muted">
        Option 1 — you fill this in with the customer in front of you. Option 2 sends them a link
        instead.
      </p>

      <form className="panel" onSubmit={onSubmit}>
        <h3 className="section-title">Details</h3>

        <TextField
          id="insured-name"
          label="Insured name"
          value={insuredName}
          onChange={(event) => setInsuredName(event.target.value)}
          required
        />

        <SelectField
          id="insurance-type"
          label="Insurance type"
          options={config.data?.insuranceTypes ?? []}
          placeholder="Choose a type"
          hint="This choice decides where the email goes."
          value={insuranceType}
          onChange={(event) => setInsuranceType(event.target.value)}
          required
        />

        <TextArea
          id="insured-address"
          label="Address"
          value={insuredAddress}
          onChange={(event) => setInsuredAddress(event.target.value)}
          required
        />

        <MoneyField
          id="car-value"
          label="Car value"
          value={carValue}
          onChange={(event) => setCarValue(event.target.value)}
          required
        />

        <MoneyField
          id="estimated-premium"
          label="Estimated premium"
          value={estimatedPremium}
          onChange={(event) => setEstimatedPremium(event.target.value)}
          required
        />

        <DateField
          id="effective-date"
          label="Effective date"
          value={effectiveDate}
          onChange={(event) => setEffectiveDate(event.target.value)}
          required
        />

        {failed ? <AlertBanner>{failed}</AlertBanner> : null}

        <Button type="submit" variant="primary" disabled={!complete || mutation.isPending}>
          {mutation.isPending ? 'Saving…' : 'Save details'}
        </Button>
        <span className="caption">
          All six are needed. Documents are attached on the next screen, before this goes to AXA.
        </span>
      </form>
    </section>
  )
}

function describe(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.body.includes('unknown_insurance_type')) {
      return 'That insurance type is no longer offered. Choose another from the list.'
    }

    if (error.body.includes('invalid_amount')) {
      return 'The car value and the estimated premium must both be more than zero.'
    }

    if (error.body.includes('incomplete_request')) {
      return 'All six details are needed before this can be saved.'
    }

    return `Not saved (${error.status}). Check the connection and try again.`
  }

  return 'Not saved. Check the connection and try again.'
}
