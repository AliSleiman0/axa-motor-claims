# Scope decisions

Status: **proposed by developer, not yet acknowledged by client.**
These must be confirmed in writing (scope letter) before code starts.

## In scope — 8 weeks

- **Admin + onboarding** — CRUD for all 4 profile types, invite links by SMS, phone-OTP login, active/inactive, NEXT3 ID mapping
- **Expert module (full)** — claim notification, claim detail view (visa, policy, plate, insured, phone, car make/model, city), "Arrived" button writing date/time/location back to NEXT3, photo capture, voice note, car body damage diagram, the 4 document buckets, quality check with retry, search past claims by visa or plate, expert report upload
- **Garage + Claim Officer survey flow (full)** — new claim declaration, upload, officer notification, visa search in NEXT3, approve/reject with comments, approval rendered as an image into the NEXT3 Survey folder, garage notification, post-repair uploads (discharge, invoice, repair photos)
- **Broker Option 1** — form (insured name, insurance type, address, car value, estimated premium, effective date) + document upload/capture, submit, email routed to the AXA recipient for that insurance type
- **NEXT3 integration** against client-provided API endpoints

### The 4 document buckets (Expert)
Per BRD: car photos are **capture-only** — upload must be disabled for them.

| Bucket | Upload | Capture |
|---|---|---|
| Insured Documents | yes | yes |
| Insured Car Photo | **no** | yes |
| TP Documents | yes | yes |
| TP Car Photo | **no** | yes |

The same split applies to the Garage module (documents vs car photos).

## Out of scope — must be excluded in writing

| Excluded | Note |
|---|---|
| **Broker Option 2** (client-link self-service capture flow) | A second product surface for unauthenticated end-customers. Primary negotiating chip — trade this away first. |
| **Offline mode** | Not in the BRD. Genuinely needed for roadside experts — flag as a known limitation and propose as phase 2. |
| **Native app store builds** | Deliverable is a PWA. |
| **Arabic / RTL, French** | English only unless separately funded. RTL is not free. |
| **Production SLA / 24-7 support** | Offer a defined hypercare window instead (e.g. 2 weeks, business hours). |
| **Reporting / MIS / dashboards** | Not requested in the BRD; will be asked for. |
| **Voice transcription** | Record, upload, store. Nothing more. |

## Simplifications — where the BRD is vague

| BRD text | Interpretation | Effort |
|---|---|---|
| *"Image visibility and voice clarity must be ensured"* | Client-side resolution check + blur-variance threshold + user confirm screen. **Not ML.** | ~0.5 day |
| *"a car body diagram where the expert can mark the accident spot"* | Static SVG car, tap-to-mark hotspots, flattened to PNG via canvas | ~0.5 day |
| *"approval and comments is to be captured as an image"* | Render the comment block to canvas → PNG → push to NEXT3 | ~0.5 day |
| *"immediate or once per day"* (sync mode) | **Immediate.** Simpler to build and better UX. Confirm with client. | — |

Each interpretation must appear in the scope letter, or it will be reinterpreted during UAT.

## Commercial guardrails

- Payment: **40% up front / 30% at week-4 demo / 30% at handover**
- **Two UAT rounds.** Beyond agreed scope = separate quote.
- Deliverable = **deployed and demoed**, not "AXA has completed internal rollout" — their rollout is not on the developer's clock.
- All third-party costs (SMS gateway, hosting, domain) on **AXA accounts, AXA card**.
- Deployment target: **client-provided environment, containerized.** If AXA chooses on-prem, the additional integration work is a variation, not absorbed.
