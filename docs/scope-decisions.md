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
| §9 "refresh token" (no storage/lifecycle defined) | Rotating `refresh_token` table (design.md §4): hash stored, rotated on every use, reuse of a rotated token revokes all the user's tokens, all revoked on deactivation. Deactivation also kills live access tokens via a per-request active-user check. Invite validity and OTP resend throttle are new placeholder knobs (`Auth.InviteValidityDays`, `Auth.OtpResendSeconds`). *Added 2026-08-19, slice 1.2.* | — |
| Auth housekeeping deferred by slice | ~~Login audit events → slice 1.3~~ (done, slice 1.3); SMS sends logged to `notification` → slice 1.4; `otp_challenge` purge job → slice 2.3 (cleanup-job infra). *Added 2026-08-19, slice 1.2.* | — |
| Admin A1 semantics (§5.4 defines only create/invite/deactivate) | **No reactivate endpoint and no hard delete** — D = deactivate; an inactive user stays inactive (re-create if genuinely needed). **Phone immutable after create** — it is the OTP login identity. **Re-issue-invite endpoint added** (`POST /api/admin/users/{id}/invite`, allowed only while status = invited) under A1's "+ invites": the 7-day invite validity makes it operationally necessary. Update allowed on inactive users (admin record-fixing). *Added 2026-08-19, slice 1.3.* | — |
| Profile `active` vs `app_user.status` (§4 has both on expert/garage) | Deactivation sets `app_user.status`/`inactivated_at` **and** the profile's `active`/`inactivated_at` in one transaction, so the two never diverge. Officer/broker profiles have no active columns per §4 — `app_user.status` alone governs them. *Added 2026-08-19, slice 1.3.* | — |
| `next3_id` before the NEXT3 mapping exists | Nullable on expert/garage profiles; uniqueness via filtered unique index (`WHERE next3_id IS NOT NULL`) — admin may create a profile before the NEXT3 ID is known (#8). *Added 2026-08-19, slice 1.3.* | — |
| `audit_log` shape (§4 marks only `actor_user_id` nullable) | `entity_id` is also nullable — a failed login for an unknown phone has no entity; the attempted phone goes in `detail` JSON. Append-only is enforced by a DB trigger (`INSTEAD OF UPDATE, DELETE`), not by an architecture rule — the trigger is the actual control; the code nudge is that `AppDbContext` exposes no `DbSet<AuditLog>` and only `AuditWriter` writes. Invite-verify activation is audited (`user_activated`) — it issues tokens, so §9's "login success/failure" covers it. *Added 2026-08-19, slice 1.3.* | — |
| Option 2 public API shape (§9.1 names only `/p/{token}`) | The API group is **`/public/{token}`**; §9.1's `/p/{token}` is the *web* route slice 5.3 puts in front of it. Same token, two surfaces. *Added 2026-08-19, slice 1.5.* | — |
| Option 2 rate-limit thresholds (§9.1 mandates limits, names no numbers; no open question covers them) | Engineering choice, so **named config keys** rather than literals: `PublicLink.RateLimit.PerIpPermitsPerMinute` (60) and `PerTokenPermitsPerMinute` (20), chained so varying either half buys no budget. The per-token partition key is the token's **SHA-256 hash**, never the raw value — a partition key reaches logs and dumps, and hashing also makes an invalid token throttle exactly like a real one, so the limiter is not an enumeration oracle. Retunable after #21 without a rebuild. *Added 2026-08-19, slice 1.5.* | — |
| What P1 shows before submission (§9.1 says "the broker's display name") | **No broker identity is returned in this slice.** The display name lives on `app_user`, which architecture rule 2 puts out of the public module's reach. Recommendation for 5.3: denormalise `broker_display_name` onto `broker_request` at B3 time rather than weakening the boundary. *Added 2026-08-19, slice 1.5.* | — |
| `broker_request.expired` state (§5.3 lists it; nothing sets it) | **Not written by slice 1.5** — there is no expiry job. `public_link_token.expires_at` governs the public side and the uniform 404 covers it; the broker-facing lazy transition lands with B1 in 5.3, together with the index that sweep will need. *Added 2026-08-19, slice 1.5.* | — |
| Option 2 submit preconditions before the media pipeline exists | Submit validates **the six fields only**. Required documents and the five mandatory car shots (§5.3) cannot be enforced before slices 2.3/6.1, and an incomplete submit returns 400 **without** locking the token, so a mistyped form does not burn the customer's link. No broker push on submit yet (§5.3's notification lands with B4 in 5.3). *Added 2026-08-19, slice 1.5.* | — |
| Upload caps with nowhere yet to put a file | Caps land as a unit-tested pure guard (`PublicUploadCaps`) plus **middleware** — not an endpoint filter, which runs after model binding and so rejected an oversize body as malformed JSON instead of as a cap breach. Middleware also lowers `IHttpMaxRequestBodySizeFeature`, so a caller who lies about `Content-Length` is cut off by the server. Byte-accurate per-file counting arrives with 2.3. **The middleware caps the whole request at one file's worth (`MaxFileMb`), which is right while only JSON flows through and wrong the moment 5.3/6.1 posts a multipart submission of up to `MaxFiles` files — the request ceiling must rise to ~`MaxFiles × MaxFileMb` at that point, with `PublicUploadCaps` doing the per-part accounting.** *Added 2026-08-19, slice 1.5.* | — |
| FK delete behaviour on the new tables (§4 is silent) | `broker_request → app_user` is **Restrict** (EF defaults a required FK to Cascade, which would let a future user delete silently take a broker's request history); `public_link_token → broker_request` is **Cascade**, because a capability without its request is meaningless. *Added 2026-08-19, slice 1.5.* | — |
| Admin browser pages (no BRD definition) | Functional, unstyled React pages; JWT pair in `localStorage` with refresh-on-401; single-fetch wrapper, no state library. *Added 2026-08-19, slice 1.3.* | — |

Each interpretation must appear in the scope letter, or it will be reinterpreted during UAT.

## Commercial guardrails

- Payment: **40% up front / 30% at week-4 demo / 30% at handover**
- ~~Two UAT rounds~~ **One UAT round** (2026-08-19 — the second round was spent to fund Broker Option 2, see `design.md` §11). This spends the defence against UAT scope creep, so round 1 must be tightly defined in the scope letter: participants, acceptance criteria, capped defect-fix window. Beyond agreed scope = separate quote.
- Deliverable = **deployed and demoed**, not "AXA has completed internal rollout" — their rollout is not on the developer's clock.
- All third-party costs (SMS gateway, hosting, domain) on **AXA accounts, AXA card**.
- Deployment target: **client-provided environment, containerized.** If AXA chooses on-prem, the additional integration work is a variation, not absorbed.
