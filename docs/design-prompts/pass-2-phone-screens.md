# Design prompt — Pass 2: Phone screens

**How to run:** after pass 1 is settled, type `/design` and paste everything below the line. If this is a fresh session without the pass-1 canvas in context, paste the pass-1 token summary and component names at the top of the brief so the screens reuse them rather than reinventing them.

---

Design the PHONE screens for the AXA Motor Claims app described in docs/design.md (loaded in context), using the foundations from pass 1 — reuse its component names and tokens verbatim; do not redraw components. This is PASS 2 of 3.

PRODUCT & USERS
- Expert and Garage use a Capacitor-wrapped app at crash sites and workshops, one-handed, often in sunlight; the Option 2 public customer opens one page from a link with no login. All artboards 390×844. Tap targets ≥ 44 px. Tone: calm, institutional insurer.

HARD CONSTRAINTS (from the build — do not design around them)
- NEUTRAL PALETTE, NO AXA BRANDING; placeholder wordmark only.
- Every data value obviously fake: PLACEHOLDER-VISA-0001, PLC-TEST-01, PLACEHOLDER Insured One, +999 000 000 001.
- Capture-only buckets show a camera action only; document buckets show camera + file. Labels distinct per bucket, with counts.
- Clarity confirm is the shared component (image / audio / file variants).
- Outage is a first-class state: the "NEXT3 unreachable — showing last known details" banner, and a "queued, will send" indicator on documents. The app never blocks the field user.
- Dates formatted local time; missing values an em dash.
- Every screen annotated with ALL its states, not just the happy path.

DELIVER NOW — PASS 2: PHONE SCREENS
1. S1 Invite / registration: link opened → phone confirmation → OTP entry → done. States: expired invite, wrong code, too many attempts.
2. S2 Login: two steps — phone, then code. States: code sent, code rejected, resend throttled ("try again in 42 s"), unsupported browser note for push.
3. E1 Claim list: search field (visa or plate) ABOVE the list, never unmounted while loading; rows with visa, plate, insured, vehicle, accident date, received, media count, arrived. States: loading, empty ("No claims assigned yet"), no match for the search term, cold-cache hint ("plate unknown until the claim has loaded").
4. E2 Claim detail: claim key-value table; banners for stale ("showing last known details") and not-found; Arrival section with the Arrived button in its states — ready / sending / arrived at {time} / location denied (blocking explanation) / location timed out; beneath it the capture section: five buckets (Insured documents, Insured car photos — camera only, Third-party documents, Third-party car photos — camera only, Expert report) each with count, then the voice-note panel (idle / recording / finishing / listen-before-sending) and the damage-diagram panel (diagram / marked caption / Clear / Use this diagram / the "drawn at 1600×1200" floor notice). Show the clarity-confirm overlay for a photo, and the rejected state ("too blurry — hold the phone still…").
5. Push panel placement in the expert header area, in its four states.
6. G1 Declaration worklist: rows with state chip, plate, created, media count; "New declaration" action. States: loading, empty.
7. G2 New declaration: plate (required), insured name, note; Create. Validation state.
8. G3 Declaration detail in ALL FIVE STATES on separate artboards: Draft (document bucket camera+file, car photos camera-only, counts, Submit to AXA disabled until one document exists, enabled, pending); Waiting for AXA; Not accepted (status only — deliberately NO comments shown, by rule); Approved (unlocked claim details table, officer comments, Start repairs); Repairs in progress (repair uploads: repair photos camera-only, discharge and invoice camera+file; Submit repair documents).
9. G4 Repair documents submitted: terminal confirmation state.
10. P1 Public customer page (no header nav, no login): intro with the broker's name; six fields (insured name, insurance type as a select, address, car value, estimated premium, effective date); supporting documents (camera + file); the five mandatory car-side captures chosen on the car diagram (front, rear, left, right, roof) each with done/not-done marks; clarity confirm; Submit disabled until all five shots exist; success screen; and the "link expired or already used" page (plain, no details).
11. A single "phone navigation" artboard: bottom nav for Expert (Claims, Notifications) and Garage (Declarations, Notifications).
