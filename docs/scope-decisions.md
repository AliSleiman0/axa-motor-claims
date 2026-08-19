# Scope decisions

Status: **proposed by developer, not yet acknowledged by client.**
These must be confirmed in writing (scope letter) before code starts.

## In scope — 8 weeks

- **Admin + onboarding** — CRUD for all 4 profile types, invite links by SMS, phone-OTP login, active/inactive, NEXT3 ID mapping
- **Expert module (full)** — claim notification, claim detail view (visa, policy, plate, insured, phone, car make/model, city), "Arrived" button writing date/time/location back to NEXT3, photo capture, voice note, car body damage diagram, the 4 document buckets, quality check with retry, search past claims by visa or plate, expert report upload
- **Garage + Claim Officer survey flow (full)** — new claim declaration, upload, officer notification, visa search in NEXT3, approve/reject with comments, approval rendered as an image into the NEXT3 Survey folder, garage notification, post-repair uploads (discharge, invoice, repair photos)
- **Broker Option 1** — form (insured name, insurance type, address, car value, estimated premium, effective date) + document upload/capture, submit, email routed to the AXA recipient for that insurance type
- **Broker Option 2** — **added 2026-08-19 at the developer's decision.** The broker sends a link to the customer's mobile number; the customer completes the same fields, uploads supporting documents, and captures the four car sides plus roof with side selection and a clarity check; the completed file returns to the broker's profile as *ready to send*; the broker reviews and triggers the email. Builds on Option 1 — same fields, same insurance-type list, same email routing.
- **NEXT3 integration** against client-provided API endpoints

> **Consequence of taking Option 2 in scope, recorded honestly:** it adds an estimated **1.5–2.5 weeks** to an 8-week plan that already covered only ~60–70% of the BRD, and it was the primary negotiating chip — that chip is now spent. It is also the project's largest security surface: a public, unauthenticated page collecting identity documents from members of the public.
>
> **How it was paid for (decided 2026-08-19, see `design.md` §11):** the calendar holds at 8 weeks. **Damage-diagram polish and the second UAT round are dropped**; the rest is absorbed by reusing the expert capture/clarity/upload pipeline for the public flow. **There is no remaining descope lever** — any further slip (late NEXT3 sandbox, InfoSec review on our clock, NEXT3 vendor delay) moves the date day-for-day, and that is said to the client now.

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
| ~~**Broker Option 2**~~ | **MOVED INTO SCOPE 2026-08-19.** See the in-scope list above. |
| **Offline mode** | Not in the BRD. Genuinely needed for roadside experts — flag as a known limitation and propose as phase 2. |
| ~~**Native app store builds**~~ | **SUPERSEDED 2026-08-19** by the Capacitor decision (HANDOFF §4, `design.md` §1): delivery is the React app in a Capacitor wrapper with store/MDM distribution (provisional pending `research-capacitor.md` + the week-6 device checkpoint), plus browser/PWA for desktop roles. Open question #23 is superseded the same way. |
| **Arabic / RTL, French** | English only unless separately funded. RTL is not free. |
| **Production SLA / 24-7 support** | Offer a defined hypercare window instead (e.g. 2 weeks, business hours). |
| **Reporting / MIS / dashboards** | Not requested in the BRD; will be asked for. |
| **Voice transcription** | Record, upload, store. Nothing more. |
| **Damage-diagram polish** | **Dropped 2026-08-19** to hold 8 weeks with Option 2 in. Diagram ships functional: static SVG, tap-to-mark, PNG export. |
| **Second UAT round** | **Dropped 2026-08-19**, same trade. One UAT round, tightly defined (see Commercial guardrails). |

## Simplifications — where the BRD is vague

| BRD text | Interpretation | Effort |
|---|---|---|
| *"Image visibility and voice clarity must be ensured"* | Client-side resolution check + blur-variance threshold + user confirm screen. **Not ML.** | ~0.5 day |
| *"a car body diagram where the expert can mark the accident spot"* | Static SVG car, tap-to-mark hotspots, flattened to PNG via canvas | ~0.5 day |
| *"approval and comments is to be captured as an image"* | Render the comment block to canvas → PNG → push to NEXT3 | ~0.5 day |
| *"immediate or once per day"* (sync mode) | **Immediate.** Simpler to build and better UX. Confirm with client. | — |
| Rejection handling (BRD: "Disregard Case", no resubmit path) | Rejection is **terminal** — the garage files a new declaration if needed. No NEXT3 push on rejection (matches diagram 02). Rejection comments stored but not shown to the garage (BRD grants comment visibility only on confirmation). *Added 2026-08-19, `design.md` §5.2.* | — |
| Declaration lifecycle end | Ends at *repair documents submitted* — no closure/settlement state; the BRD defines none. *Added 2026-08-19.* | — |
| Garage declaration form fields (BRD defines none) | **Plate number (required)** — the officer's claim-search key — plus optional insured name and note. *Added 2026-08-19.* | — |
| Expert "done" signal (BRD defines none) | None built — an assignment accumulates media indefinitely. *Added 2026-08-19.* | — |
| Option 2 Estimated Premium | **Kept as the BRD writes it: the customer enters it** (decision 2026-08-19; flagged #24c — if AXA moves it broker-side the change is form-level). | — |

Each interpretation must appear in the scope letter, or it will be reinterpreted during UAT.

## Commercial guardrails

- Payment: **40% up front / 30% at week-4 demo / 30% at handover**
- ~~Two UAT rounds~~ **One UAT round** (2026-08-19 — the second round was spent to fund Broker Option 2, see `design.md` §11). This spends the defence against UAT scope creep, so round 1 must be tightly defined in the scope letter: participants, acceptance criteria, capped defect-fix window. Beyond agreed scope = separate quote.
- Deliverable = **deployed and demoed**, not "AXA has completed internal rollout" — their rollout is not on the developer's clock.
- All third-party costs (SMS gateway, hosting, domain) on **AXA accounts, AXA card**.
- Deployment target: **client-provided environment, containerized.** If AXA chooses on-prem, the additional integration work is a variation, not absorbed.
