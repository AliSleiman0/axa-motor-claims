# Open questions for AXA

**None of these have been answered yet.** Do not invent values for any TBC item.

Send **#1, #2, #21, #23 today** — those four decide whether 2 months is real.

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
| 8 | The expert extract (NEXT3 ID + name + phone) — when, what format, one-time or synced? | Onboarding | TBC |

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
| 24 | Confirm **Broker Option 2 is out of scope** for this phase | TBC |
| 25 | UAT — who participates, when, how many rounds, what counts as acceptance? | TBC |
| 26 | Does the existing email process run in parallel after go-live, and for how long? | TBC |
| 27 | Support after handover — hours, duration, paid or not? | TBC |

## Noted gaps in the BRD (raise, but not blocking)

- No NFRs at all — no availability, performance, or concurrency targets
- No security/privacy requirements, despite third-party PII, geolocation, and photo storage
- No audit trail requirement — mandatory in claims disputes (who uploaded which photo, when)
- No UI/UX, mockups, or branding supplied
- No cutover plan from the current email process
- Broker Option 2 has the **client** entering their own "Estimated Premium" — business rule looks wrong, confirm
- Prerequisite bullet in the BRD is truncated mid-sentence: *"visibility on fields that will be updated in NEXT3 ex"*
