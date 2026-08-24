import { useRef, useState, type FormEvent } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { ApiError } from '../api/client'
import { formatDateTime } from '../api/datetime'
import { type CreatedLink, brokerKeys, createLink } from '../broker/api'
import { useBrokerConfig } from '../broker/useBroker'
import { AlertBanner, StatusBanner } from '../ui/Banner'
import { Button } from '../ui/Button'
import { DetailTable } from '../ui/DetailTable'
import { PhoneField, SelectField } from '../ui/fields'

/**
 * B3 (design.md §5.3) — the broker issues a customer link for Option 2.
 *
 * **The link is shown exactly once and it is said out loud.** §9.1 stores only the SHA-256 hash, so
 * nobody, including AXA, can recover it from the database; a broker who loses it issues a new one,
 * which is a new request and a new row. The screen is built around that fact rather than apologising
 * for it.
 */
export default function BrokerLinkPage() {
  const [issued, setIssued] = useState<CreatedLink | null>(null)

  return (
    <section className="page">
      <Link to="/broker">← Requests</Link>
      <h2 className="page__title">Send customer link</h2>
      <p className="muted">
        Option 2 — the customer fills in their own details and photographs their own car, then the
        file comes back to you to review and send.
      </p>

      {issued ? <IssuedLink link={issued} /> : <CreateForm onIssued={setIssued} />}
    </section>
  )
}

function CreateForm({ onIssued }: { onIssued: (link: CreatedLink) => void }) {
  const queryClient = useQueryClient()
  const config = useBrokerConfig()
  const [customerMobile, setCustomerMobile] = useState('')
  const [insuranceType, setInsuranceType] = useState('')
  const [failed, setFailed] = useState<string | null>(null)
  const inFlight = useRef(false)

  const mutation = useMutation({
    mutationFn: () =>
      createLink({
        customerMobile: customerMobile.trim() || null,
        insuranceType: insuranceType || null,
      }),
  })

  function onSubmit(event: FormEvent) {
    event.preventDefault()
    if (inFlight.current) return

    inFlight.current = true
    setFailed(null)

    mutation.mutate(undefined, {
      onSuccess: async (link) => {
        await queryClient.invalidateQueries({ queryKey: brokerKeys.lists() })
        onIssued(link)
      },
      onError: (error) => {
        inFlight.current = false
        setFailed(describe(error))
      },
    })
  }

  return (
    <form className="panel" onSubmit={onSubmit}>
      <PhoneField
        id="customer-mobile"
        label="Customer mobile"
        placeholder="+999000007001"
        hint="Optional, and stored only so you know whose link this is. The page itself never shows it."
        value={customerMobile}
        onChange={(event) => setCustomerMobile(event.target.value)}
      />

      <SelectField
        id="link-insurance-type"
        label="Insurance type (optional)"
        options={config.data?.insuranceTypes ?? []}
        placeholder="Let the customer choose"
        value={insuranceType}
        onChange={(event) => setInsuranceType(event.target.value)}
      />

      {failed ? <AlertBanner>{failed}</AlertBanner> : null}

      <Button type="submit" variant="primary" disabled={mutation.isPending}>
        {mutation.isPending ? 'Creating…' : 'Create link'}
      </Button>
    </form>
  )
}

function IssuedLink({ link }: { link: CreatedLink }) {
  const [copied, setCopied] = useState(false)

  // The API answers with its own path (`/public/{token}`); `/p/{token}` is the friendlier web address
  // in front of it, and the origin is whatever this app is served from. Not `Auth:AppBaseUrl`, which
  // is the server's copy for the invitation SMS: a customer opening this link is opening *this* app.
  const url = `${window.location.origin}/p/${link.token}`

  function copy() {
    void navigator.clipboard?.writeText(url).then(() => setCopied(true))
  }

  return (
    <section className="panel">
      <h3 className="panel__title">The link, once</h3>
      <p className="mono break-all">{url}</p>

      <div className="actions">
        <Button variant="primary" onClick={copy}>
          {copied ? 'Copied' : 'Copy link'}
        </Button>
        {/* Disabled, and the reason is named rather than left as a mystery control: the delivery
            channel is `PublicLink.DeliveryChannel = copy` until #24a names an SMS provider. */}
        <Button variant="secondary" disabled>
          Send by SMS
        </Button>
      </div>

      <StatusBanner>
        Copy this now — it cannot be shown again. If you lose it, create a new link; the old one
        keeps working until it expires.
      </StatusBanner>

      <p className="caption">
        Send by SMS is off — delivery is set to copy. Turning it on is a configuration change once
        AXA names an SMS provider.
      </p>

      <DetailTable
        rows={[
          { label: 'Expires', value: formatDateTime(link.expiresAt) },
          { label: 'Request', value: link.requestId, mono: true },
        ]}
      />

      <p className="caption">
        Valid for seven days, and dead the moment the customer sends it. Only a hash of it is stored,
        so nobody — including AXA — can recover the link from the database.
      </p>

      <Link className="btn btn--secondary" to="/broker">
        Back to requests
      </Link>
    </section>
  )
}

function describe(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.body.includes('invalid_phone')) {
      return 'That is not a valid mobile number. Use the international form, starting with the country code.'
    }

    if (error.body.includes('unknown_insurance_type')) {
      return 'That insurance type is no longer offered. Choose another, or let the customer choose.'
    }

    return `The link was not created (${error.status}). Check the connection and try again.`
  }

  return 'The link was not created. Check the connection and try again.'
}
