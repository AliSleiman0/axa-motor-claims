# Open questions for AXA

**None of these have been answered yet.** Do not invent values for any TBC item.

Send **#1, #2, #21, #31 today** — those four decide whether 2 months is real. Add **#23, #28–30** (mobile delivery) and **#37** (Azure access) to the same email; all are on the critical path.

## Blocking — needed in week 1, or delivery slips day-for-day

| # | Question | Blocks | Answer |
|---|---|---|---|
| 1 | NEXT3 API spec (Swagger/Postman), **sandbox URL + credentials**, auth method (OAuth / API key / mTLS), rate limits, test data | Everything from week 3 | TBC |
| 2 | Are the endpoints internet-reachable or internal-only? If internal — VPN, IP allowlist, or outbound tunnel, and **who configures it**? | Architecture + hosting | TBC |
| 3 | Who hosts production, and who owns/pays the accounts? | Architecture | TBC |
| 4 | **Is NEXT3 the permanent store for photos, or does the app retain them?** | Storage design, retention, monthly cost | TBC |
| 5 | Document upload: an API endpoint, or a shared directory + DB insert? (BRD prerequisite says *"identify the directory to upload photos and insert records"* — which is it?) Max file size? | Upload pipeline | TBC |
| 6 | Exact writable fields for the "Expert Arrived" update — field names, endpoint, date/time/location format | Expert module | TBC |
| 7 | Does AXA have an SMS gateway for OTP, or do we procure one? Who pays? Which countries? | Login — week 1 | TBC |
| 8 | The master-data extract (NEXT3 ID + name + phone) — when, what format, one-time or synced? **Covers experts, garages AND claim officers** — all four profiles carry a NEXT3 identity (manager review, 2026-08-19). | Onboarding | TBC |

## Needed by week 3

| # | Question | Answer |
|---|---|---|
| 9 | Confirm clarity check = resolution + blur threshold + user confirm (**not** ML). Needs sign-off — vaguest line in the BRD. | TBC |
| 10 | Voice notes: does NEXT3 accept audio? Which document type? Max duration/format? | TBC |
| 11 | Car diagram: is there an AXA-standard damage diagram / damage codes, or free-form marking? | TBC |
| 12 | Exact NEXT3 document-type codes for the "Expert documents" and "Survey" folders | TBC |
| 13 | **The email routing table** — which AXA recipient for each insurance type. Need the actual list. | TBC |
| 14 | The full insurance-type list (BRD gives *"MOTOR ALL RISK, MOTOR TOTAL LOSS, etc."*) | TBC |
| 15 | Broker IRIS codes — what are they, where does the list come from? | TBC |
| 16 | When the claim officer creates a missing visa, do they do it directly in NEXT3, or does the app need a create-visa API? (BRD implies they leave the app and retry — confirm.) | TBC |
| 17 | Sync mode — BRD leaves *"immediate or once per day"* open. Recommend **immediate**. | TBC |
| 18 | Confirm approval comments rendered as PNG is acceptable vs structured fields | TBC |
| 19 | Languages — English only? | TBC |
| 20 | **Volumes**: claims/day, expert/garage/broker counts, photos per claim, retention period | TBC |

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
| 32 | **Does NEXT3 deduplicate on a client-supplied reference ID?** Required for safe retries — on the critical path for the outbox. | TBC |
| 33 | What is NEXT3's expected availability, and are there maintenance windows? Sizes the retry backoff. | TBC |
| 34 | **How does the app learn a new visa was assigned?** The BRD says a *"back-office engine triggers the process"* without saying how it reaches the app — webhook from NEXT3 (preferred) or the app polls. Genuine hole in the BRD. | TBC |
| 35 | Does AXA have a preferred or mandated database platform for apps in their tenant, given their team supports this after handover? (Currently assuming Azure SQL.) | TBC |
| 36 | If integration turns out to be directory + database inserts rather than an API — what database engine does NEXT3 run on? | TBC |

## Azure / hosting — added 2026-08-18

| # | Question | Answer |
|---|---|---|
| 37 | **Contributor access to an Azure resource group** — often slower to obtain in a governed tenant than API credentials, and it is on the critical path. Request in week 1. | TBC |
| 38 | Which security baseline applies — is a managed **WAF** required? Front Door Premium is ~$330/month, more than the entire rest of the application. | TBC |
| 39 | Is the Enterprise Agreement discount available for this workload? | TBC |
| 40 | **Which SMS provider does AXA want for OTP?** The per-message rate is entirely provider-dependent and Lebanon varies widely. Options: a regional aggregator (**Monty Mobile** — likely best MENA rates), **Twilio** (dearest, fastest to integrate), or **Azure Communication Services** (one bill, but confirm Lebanon coverage first). An existing AXA gateway contract beats all three. Asked in the 2026-08-18 email — **do not quote a monthly SMS figure until answered.** | TBC |
| 41 | **One environment or two (test + production)?** One is cheaper; two lets fixes be demonstrated and approved without touching live claim data, and pairs with a NEXT3 sandbox alongside NEXT3 production. **Two roughly doubles the Azure figure.** Recommend two. Added to the client doc as Q19 on 2026-08-18. | TBC |

## From the manager's review of v0.4 — added 2026-08-19

| # | Question | Answer |
|---|---|---|
| 42 | **Authorization scoping — how does NEXT3 express which expert is assigned to which claim?** Without that linkage the app cannot restrict an expert to their own claims, and plate-number search becomes a way to read any claim in the system. **Cannot be solved on our side — the relationship lives in NEXT3.** Client doc Q5. Garage-to-declaration scoping is ours (declarations originate in our DB), but expert-to-claim is not. | TBC |
| 43 | **Photo rules**: max count per claim and per folder, max file size, accepted formats, resolution limits. Separately — **does AXA expect tampering/fraud detection on images?** That is a specialist capability, NOT included, quote separately if wanted. Client doc Q9. | TBC |
| 44 | **Mobile vs PC — same features or different?** Working assumption: one app, one login, both surfaces, but car-photo *capture* is inherently a phone activity (capture-only + no PC camera). Full parity would require allowing car-photo upload on PC, which contradicts the BRD's capture-only rule. Client doc Q12. | TBC |
| 45 | **Offline / out-of-coverage behaviour.** Accidents happen where there is no signal. Recommendation: expert + garage capture flows queue on-device and auto-upload on reconnect. **~1 additional week; currently OUT of scope** (`scope-decisions.md`). Far cheaper designed in than retrofitted — decide before week 2. Client doc Q13. | TBC |

**Also fixed in the client doc from that review:** the master-data extract (#8) now covers garages and claim officers, not experts alone, and the endpoint list gained `GET /garages` and `GET /claim-officers` plus `GET /experts/{id}/claims`.

## Noted gaps in the BRD (raise, but not blocking)

- No NFRs at all — no availability, performance, or concurrency targets
- No security/privacy requirements, despite third-party PII, geolocation, and photo storage
- No audit trail requirement — mandatory in claims disputes (who uploaded which photo, when)
- No UI/UX, mockups, or branding supplied
- No cutover plan from the current email process
- ~~Broker Option 2 premium rule~~ — promoted to question #24 now that Option 2 is in scope
- Prerequisite bullet in the BRD is truncated mid-sentence: *"visibility on fields that will be updated in NEXT3 ex"*
