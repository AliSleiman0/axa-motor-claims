# Open questions for AXA

**None of these have been answered yet.** Do not invent values for any TBC item.

Send **#1, #2, #21, #31 today** — those four decide whether 2 months is real. Add **#23, #28–30** (mobile delivery) and **#37** (Azure access) to the same email; all are on the critical path.

## Resolved 2026-08-31 — client's clarification-question answers + BRD v2

The client returned answers to the clarification-questions workbook (`docs/client-answers-2026-08-31/clarification-questions.xlsx`, its own `Q1`–`Q18` numbering, distinct from this file's `#`) together with a revised BRD (`brd-v2.docx`) and three reference SQL scripts. Full absorption is slice 7.3 (`docs/build-playbook.md`); this section is the resolution record, cross-referenced from the numbered rows below. Genuinely still-open items (Q1's auth method, Q17's SMS/WhatsApp account status, Q18's distribution) are **not** listed here and keep their `TBC` in the tables below.

- **Q2 → #5, closed.** Integration is **API only** — "API call". No shared-directory + DB-insert path; `RealNext3Client` stays an API client (design.md §12 #5).
- **Q3 → #32, closed.** NEXT3 already rejects a duplicate file submission directly today ("NEXT3 is giving that the file is already received") — observed behaviour, not a documented contract. Strengthens rather than changes the outbox's `clientRef` idempotency design; still worth NEXT3 stating the dedupe key formally.
- **Q4 → #34, closed (mechanism chosen, not yet built).** Assignment delivery is a **direct Oracle poll**: the app is given DB access (username/password/hostname/port/service name) and runs a query identifying new/updated claims by visa or `notification_id`, every ~15 seconds (`docs/client-answers-2026-08-31/uat-visa-event-trigger.sql` is the reference shape). The client's alternative — a DB trigger inserting into a middleware/app database on `CARS_LOSS_TOWING` inserts — is **not recommended by the client itself**. Builds as slice 7.4, an `IAssignmentSource` adapter behind the existing port, `Next3:AssignmentSource` staying `fake` until real connection details land.
- **Q5 → #42, closed.** Expert-to-claim linkage is read via the same query family (`uat-visa-search.sql`), joining `CARS_NOTIFICATION`/`CARS_LOSS_TOWING`/`CARS_SUPPLIER` on the expert id. Feeds 7.5's master-data/claim-linkage sync.
- **Q6 → #6, closed except GPS.** Arrival fields are in table `CARS_LOSS_TOWING`: `DISTRIBUTION_LOSS_ARRIVED` (Y/N) and `DISTRIBUTION_LOSS_ARRIVED_DATE` (Date), keyed on `NOTIFICATION_ID` = the app's assignment id. **No location/GPS column is named anywhere in the answer.** Recorded as a design decision, not a further question (design.md §12 #6): arrival coordinates stay an app-only record (`expert_assignment.arrival_lat/lng`, already authoritative for our own audit trail) and are simply omitted from `RecordArrival`'s NEXT3 payload once `RealNext3Client` is built against these fields.
- **Q7 → #8, closed.** Expert/garage master data is read **live** from NEXT3 via a query, not a one-time extract: a new record with `OUT_NETWORK='N'` is created in-app and the user is pushed an SMS to register; a record that stops appearing blocks that user's login until it reappears; sync cadence is once/day. Claim officers and brokers are **created in the application** with an AXA-assigned char id matching the existing core-system code (no live NEXT3 read for those two roles). The client's own note flags this against BRD v2's claim-officer changes — see the BRD v2 item below. Feeds slice 7.5.
- **Q8 → #10, closed.** NEXT3 stores voice/audio files in a server directory (not a DB blob column) and accepts both audio and image files; no format/size/type restriction beyond that was given.
- **Q9 → #43, closed.** No photo count, file-size, format, or resolution limit was stated by NEXT3. **Fraud/tamper detection on images is confirmed out of scope** — the client's own answer: "to include the feature as a standalone feature with pricing and based on impact AXA to decide." Recorded in `docs/scope-decisions.md`.
- **Q10 → #11, closed.** No AXA-standard damage diagram or code list exists; a simple side-identification diagram (which side has damage) is sufficient — confirms the existing §7 static-SVG, tap-to-mark design as-is, no rework needed.
- **Q11 → #16, closed.** Confirmed as designed: when a claim officer cannot find a visa, they create it directly in NEXT3 and retry the search in the app using the provided query (`uat-visa-search.sql`). **No create-visa API is built or needed.**
- **Q13 → #45, closed.** Offline mode confirmed out of scope for this phase; the client explicitly asked for a **standalone estimate** to be provided separately. Recorded in `docs/scope-decisions.md`.
- **Q14 → #9, closed.** The proposed clarity-gate approach (device-level resolution + blur checks, retake-and-confirm, playback-confirm for voice, no ML/content validation) is **confirmed** as sufficient.
- **Q15 → #20, closed.** Real volumes, measured over 200 days: **average 15 claims/day, maximum 32 in a single day** (raw daily counts in `docs/client-answers-2026-08-31/sheet2` of the workbook — not a flat "~100/day" as the original estimate assumed). Client recommends the app enforce a standardized/capped image size rather than relying on device defaults (already true — `Media.MaxFileMb`). User count is expected to be **higher** than the original 200 assumption once experts, garages and brokers are all onboarded — no firm number given. `design.md` §1/§10/§12 Appendix A's sizing text is updated to cite this instead of the placeholder "~100 claims/day, 200 users" assumption.
- **Q16 → #41, closed.** Two environments (Test/UAT + Production) confirmed.

**BRD v2 diff — recorded as two pending change requests, deliberately not built** (`docs/scope-decisions.md`, dated 2026-08-31; both need pricing/schedule confirmation before code, per CLAUDE.md's "every unplanned week is unpaid"):
1. Claim-officer `Role` field (`Officer`/`Manager`), a manager's ability to override a `rejected` declaration back to `approved`, and a garage-to-claim-officer default link. Not silently absorbed because §5.2's terminal-rejection rule is load-bearing elsewhere (7.2's `RejectedDeclarationBlobCleanupTask`, `CK_declaration_decision`) — reopening a rejection needs those reasoned through, not a transition bolted on.
2. Q12's per-profile capture-only override — today's `Broker.AllowUpload` is one global kill-switch; the client wants a per-expert/garage/broker exception, which is a schema + admin-UI addition, not a config tweak.

## Blocking — needed in week 1, or delivery slips day-for-day

| # | Question | Blocks | Answer |
|---|---|---|---|
| 1 | NEXT3 API spec (Swagger/Postman), **sandbox URL + credentials**, auth method (OAuth / API key / mTLS), rate limits, test data | Everything from week 3 | TBC |
| 2 | Are the endpoints internet-reachable or internal-only? If internal — VPN, IP allowlist, or outbound tunnel, and **who configures it**? | Architecture + hosting | TBC |
| 3 | Who hosts production, and who owns/pays the accounts? | Architecture | TBC |
| 4 | **Is NEXT3 the permanent store for photos, or does the app retain them?** | Storage design, retention, monthly cost | TBC |
| 5 | Document upload: an API endpoint, or a shared directory + DB insert? | Upload pipeline | **Resolved 2026-08-31: API only** — see Resolved section above |
| 6 | Exact writable fields for the "Expert Arrived" update — field names, endpoint, date/time/location format | Expert module | **Resolved 2026-08-31, except GPS** — see Resolved section above |
| 7 | Does AXA have an SMS gateway for OTP, or do we procure one? Who pays? Which countries? | Login — week 1 | TBC — client raised WhatsApp OTP as an option 2026-08-31 (registration-only use), and needs to check whether its existing SMS account is still active |
| 8 | The master-data extract (NEXT3 ID + name + phone) — when, what format, one-time or synced? **Covers experts, garages AND claim officers** — all four profiles carry a NEXT3 identity (manager review, 2026-08-19). | Onboarding | **Resolved 2026-08-31** — see Resolved section above |

## Needed by week 3

| # | Question | Answer |
|---|---|---|
| 9 | Confirm clarity check = resolution + blur threshold + user confirm (**not** ML). Needs sign-off — vaguest line in the BRD. | **Resolved 2026-08-31: confirmed** — see Resolved section above |
| 10 | Voice notes: does NEXT3 accept audio? Which document type? Max duration/format? | **Resolved 2026-08-31, partially** — accepted, stored server-side by directory not DB; no format/size/document-type limit stated — see Resolved section above |
| 11 | Car diagram: is there an AXA-standard damage diagram / damage codes, or free-form marking? | **Resolved 2026-08-31: no standard, simple side-identification confirmed sufficient** — see Resolved section above |
| 12 | Exact NEXT3 document-type codes for the "Expert documents" and "Survey" folders | TBC — not addressed by the 2026-08-31 answers |
| 13 | **The email routing table** — which AXA recipient for each insurance type. Need the actual list. | TBC |
| 14 | The full insurance-type list (BRD gives *"MOTOR ALL RISK, MOTOR TOTAL LOSS, etc."*) | TBC |
| 15 | Broker IRIS codes — what are they, where does the list come from? | TBC |
| 16 | When the claim officer creates a missing visa, do they do it directly in NEXT3, or does the app need a create-visa API? (BRD implies they leave the app and retry — confirm.) | **Resolved 2026-08-31: confirmed as designed, no create-visa API** — see Resolved section above |
| 17 | Sync mode — BRD leaves *"immediate or once per day"* open. Recommend **immediate**. | TBC — not addressed by the 2026-08-31 answers |
| 18 | Confirm approval comments rendered as PNG is acceptable vs structured fields | TBC — not addressed by the 2026-08-31 answers |
| 19 | Languages — English only? | TBC — not addressed by the 2026-08-31 answers |
| 20 | **Volumes**: claims/day, expert/garage/broker counts, photos per claim, retention period | **Resolved 2026-08-31 for claims/day; user count and retention still TBC** — see Resolved section above |

## Commercial / governance — ask now, these can break the deadline

| # | Question | Answer |
|---|---|---|
| 21 | **Will AXA Group InfoSec review this? Is a pen test required, and on whose clock?** Most likely thing to blow past 2 months. | TBC |
| 22 | Data residency — which country may the PII and photos reside in? | TBC |
| 23 | **Confirm PWA delivery is acceptable** (no app store submission). Currently a developer-side assumption — validate before building. | TBC |
| 24 | **Broker Option 2 is now IN scope (2026-08-19).** Three answers are needed before it can be built: (a) how is the link delivered to the customer — SMS from our gateway, or WhatsApp, or does the broker copy/paste it? (b) how long should the link stay valid, and may it be reused? (c) **who sets the Estimated Premium?** The BRD has the *customer* entering it, which looks wrong — customers do not price their own insurance. | TBC |
| 25 | UAT — who participates, when, how many rounds, what counts as acceptance? | TBC |
| 26 | Does the existing email process run in parallel after go-live, and for how long? | TBC |
| 27 | Support after handover — hours, duration, paid or not? | TBC |

## Mobile delivery — needed before week 6 (added 2026-08-18)

| # | Question | Answer |
|---|---|---|
| 28 | **What is the device mix across the expert and garage network — roughly what share is Android vs iOS?** If 80%+ Android, the iOS push risk shrinks sharply and Android-only Capacitor needs no Mac at all. | TBC |
| 29 | Should the apps go to the public App Store / Play Store, or be distributed internally via MDM? Internal distribution removes review cycles entirely. | TBC |
| 30 | Does AXA already hold an Apple Developer / Enterprise account? If not, provisioning one is a multi-week corporate process. | TBC |

## NEXT3 platform — added 2026-08-18

| # | Question | Answer |
|---|---|---|
| 31 | **Is NEXT3 a commercial product (which vendor and version?) or developed in-house? Who maintains it, and will the API work be done by your team or a third-party vendor — is it already budgeted and scheduled?** Tells us whether "they'll provide endpoints" means two weeks or two months. | TBC |
| 32 | **Does NEXT3 deduplicate on a client-supplied reference ID?** Required for safe retries — on the critical path for the outbox. | **Resolved 2026-08-31, observed rather than documented** — see Resolved section above |
| 33 | What is NEXT3's expected availability, and are there maintenance windows? Sizes the retry backoff. | TBC — not addressed by the 2026-08-31 answers |
| 34 | **How does the app learn a new visa was assigned?** The BRD says a *"back-office engine triggers the process"* without saying how it reaches the app — webhook from NEXT3 (preferred) or the app polls. Genuine hole in the BRD. | **Resolved 2026-08-31: direct Oracle poll, ~15s interval** (webhook explicitly not offered) — see Resolved section above; **adapter built 2026-09-01, slice 7.4** (`OraclePollAssignmentSource`, `Next3:AssignmentSource=oracle-poll`), unexercised against a live Oracle connection until real credentials arrive |
| 35 | Does AXA have a preferred or mandated database platform for apps in their tenant, given their team supports this after handover? (Currently assuming Azure SQL.) | TBC |
| 36 | If integration turns out to be directory + database inserts rather than an API — what database engine does NEXT3 run on? | TBC |

## Azure / hosting — added 2026-08-18

| # | Question | Answer |
|---|---|---|
| 37 | **Contributor access to an Azure resource group** — often slower to obtain in a governed tenant than API credentials, and it is on the critical path. Request in week 1. | TBC |
| 38 | Which security baseline applies — is a managed **WAF** required? Front Door Premium is ~$330/month, more than the entire rest of the application. | TBC |
| 39 | Is the Enterprise Agreement discount available for this workload? | TBC |
| 40 | **Which SMS provider does AXA want for OTP?** The per-message rate is entirely provider-dependent and Lebanon varies widely. Options: a regional aggregator (**Monty Mobile** — likely best MENA rates), **Twilio** (dearest, fastest to integrate), or **Azure Communication Services** (one bill, but confirm Lebanon coverage first). An existing AXA gateway contract beats all three. Asked in the 2026-08-18 email — **do not quote a monthly SMS figure until answered.** | TBC |
| 41 | **One environment or two (test + production)?** One is cheaper; two lets fixes be demonstrated and approved without touching live claim data, and pairs with a NEXT3 sandbox alongside NEXT3 production. **Two roughly doubles the Azure figure.** Recommend two. Added to the client doc as Q19 on 2026-08-18. | **Resolved 2026-08-31: two, confirmed** — see Resolved section above |

## From the manager's review of v0.4 — added 2026-08-19

| # | Question | Answer |
|---|---|---|
| 42 | **Authorization scoping — how does NEXT3 express which expert is assigned to which claim?** Without that linkage the app cannot restrict an expert to their own claims, and plate-number search becomes a way to read any claim in the system. **Cannot be solved on our side — the relationship lives in NEXT3.** Client doc Q5. Garage-to-declaration scoping is ours (declarations originate in our DB), but expert-to-claim is not. | **Resolved 2026-08-31: query provided** — see Resolved section above; feeds slice 7.5 |
| 43 | **Photo rules**: max count per claim and per folder, max file size, accepted formats, resolution limits. Separately — **does AXA expect tampering/fraud detection on images?** That is a specialist capability, NOT included, quote separately if wanted. Client doc Q9. | **Resolved 2026-08-31: no NEXT3-side limits stated; fraud detection confirmed out of scope, priced separately if wanted** — see Resolved section above |
| 44 | **Mobile vs PC — same features or different?** Working assumption: one app, one login, both surfaces, but car-photo *capture* is inherently a phone activity (capture-only + no PC camera). Full parity would require allowing car-photo upload on PC, which contradicts the BRD's capture-only rule. Client doc Q12. | **Resolved 2026-08-31, as a change request rather than a plain confirm**: client wants the capture-only rule made a **per-profile override** (per expert/garage/broker exception), not a plain yes/no — recorded as a pending change request in `docs/scope-decisions.md`, not built |
| 45 | **Offline / out-of-coverage behaviour.** Accidents happen where there is no signal. Recommendation: expert + garage capture flows queue on-device and auto-upload on reconnect. **~1 additional week; currently OUT of scope** (`scope-decisions.md`). Far cheaper designed in than retrofitted — decide before week 2. Client doc Q13. | **Resolved 2026-08-31: confirmed out of scope, standalone estimate requested** — see Resolved section above |
| 46 | **Does NEXT3 distinguish documents by file name?** Every photo captured on an iPhone arrives named `image.jpg` (Safari gives no other name), so several photos under one visa share a name. The app already distinguishes them by document-type code (#12) and by the `clientRef` it sends with each push (#32) — AXA needs to confirm NEXT3 does not key on the name. *Added 2026-08-23, device checkpoint (slice 6.3a).* | TBC |
| 47 | **Which currency are car value and estimated premium in?** The BRD names the fields but no currency; the app shows bare amounts (no symbol) until told — **built that way in slice 5.2**: `MoneyField` renders the number and nothing else, on B2 and, from 5.3, on P1. One value per deployment, or per insurance type? *Added 2026-08-23, week-5 expansion (B2/P1 forms).* | TBC |
| 48 | **Must an Option 1 broker request carry at least one document before it is emailed?** B2's caption promises "the details and every document above", but the BRD names no minimum and the submit currently sends with none attached (week-5 browser-pass finding 5). Option 2's public submit does require one (`documents_required`, slice 5.3) — should Option 1 match? *Added 2026-08-24, week-5 browser pass.* | TBC |
| 49 | **Which Firebase project will own Android push in production, and who holds its service-account key?** Android notifications go through Firebase Cloud Messaging (slice 6.3), which needs two things AXA must own by handover: a **Firebase project** with the app registered against the package name `com.axa.motorclaims`, and a **service-account key** the server signs every send with. Both are the developer's today — the project is developer-owned by decision (2026-08-24) so the build was not blocked, and §10's rule is that every account is in AXA's name on AXA's card from day one, which this is not yet. Two consequences worth stating: the service-account key is a real credential (whoever holds it can send notifications Android accepts as coming from AXA) and belongs in Container Apps secrets, never a repository; and **swapping projects means a new `google-services.json` and therefore a new APK on every handset**, not a config change — so the later it happens, the more it costs. Related: `Push:Vapid:Subject` must also become a real routable AXA mailbox before any iPhone can receive a push at all (Apple 403s a reserved contact, silently — 6.3a). *Added 2026-08-26, slice 6.3.* | TBC |

**Also fixed in the client doc from that review:** the master-data extract (#8) now covers garages and claim officers, not experts alone, and the endpoint list gained `GET /garages` and `GET /claim-officers` plus `GET /experts/{id}/claims`.

## Noted gaps in the BRD (raise, but not blocking)

- No NFRs at all — no availability, performance, or concurrency targets
- No security/privacy requirements, despite third-party PII, geolocation, and photo storage
- No audit trail requirement — mandatory in claims disputes (who uploaded which photo, when)
- No UI/UX, mockups, or branding supplied
- No cutover plan from the current email process
- ~~Broker Option 2 premium rule~~ — promoted to question #24 now that Option 2 is in scope
- Prerequisite bullet in the BRD is truncated mid-sentence: *"visibility on fields that will be updated in NEXT3 ex"*
