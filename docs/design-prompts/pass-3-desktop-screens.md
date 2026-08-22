# Design prompt — Pass 3: Desktop screens

**How to run:** after pass 2, type `/design` and paste everything below the line. In a fresh session, paste the pass-1 token summary and component names at the top so the desktop screens reuse them.

---

Design the DESKTOP screens for the AXA Motor Claims app described in docs/design.md (loaded in context), using the foundations from pass 1 — reuse its component names and tokens verbatim. This is PASS 3 of 3. Artboards 1440×900. These roles sit at a desk: denser tables, more on screen, keyboard-friendly.

HARD CONSTRAINTS (from the build — do not design around them)
- NEUTRAL PALETTE, NO AXA BRANDING; placeholder wordmark only.
- Every data value obviously fake: PLACEHOLDER-VISA-0001, PLC-TEST-01, PLACEHOLDER Insured One, PLACEHOLDER-recipient-1@example.invalid, insurance types "MOTOR ALL RISK" / "MOTOR TOTAL LOSS" / "PLACEHOLDER-TYPE-3".
- Outage is a first-class state: "NEXT3 unreachable" banners where a screen depends on it (visa search, approve). Approve cannot proceed while NEXT3 is unreachable — show that state honestly.
- Dates formatted local time; missing values an em dash.
- Every screen annotated with ALL its states, not just the happy path.

DELIVER NOW — PASS 3: DESKTOP SCREENS
1. Desktop frame: top navigation per role (Claim Officer: Inbox; Broker: Requests; Admin: Experts / Garages / Claim officers / Brokers / Failed pushes), role name, sign-out.
2. O1 Declaration inbox: newest first; columns garage contact, plate, submitted at, media count, state chip. States: loading, empty ("Nothing waiting").
3. O2 Declaration review — the most important desktop screen: left column declaration fields and garage contact; centre the documents with inline image previews and named links for PDF/audio (never a broken-image box); right column the decision panel: visa search (plate or visa → results table → "Use this visa"), the not-found hint ("Create the visa in NEXT3, then search again"), the NEXT3-unreachable state, a comments textarea, Approve / Reject with pending labels, and the sequence notice while approving ("Rendering decision… Uploading… Approving…"). Also the post-decision read-only state.
4. B1 Request list: columns insured name, insurance type, option (1 / 2), state chip (Draft, Submitted, Link issued, Customer in progress, Ready to send, Sent, Expired), created. "New request" and "Send customer link" actions.
5. B2 New request (Option 1): six fields; documents and photos with camera + file and an "uploaded / captured" provenance tag on each; the upload control hidden when the upload kill-switch is off (show both variants); Submit → confirmation naming the routed recipient placeholder.
6. B3 Create customer link (Option 2): customer mobile number, insurance type preset optional; the generated link shown ONCE with a copy action and a "we cannot show this again" note; the SMS-send variant disabled with "delivery channel: copy" explanation.
7. B4 Review + Send Email: the customer's submission read-only (six fields, documents, the five car-side photos in a grid labelled front/rear/left/right/roof), Send Email with pending label, sent confirmation. State: customer still in progress (nothing to review yet).
8. A1 Profile management: list per profile type with active/inactive and invited state; create/edit form for each type (Expert: mobile, name, NEXT3 ID, email, active; Garage: contact, phone, mobile, email, NEXT3 ID, address, opening hours; Claim officer: NEXT3 user, name, mobile, email; Broker: IRIS code, name, mobile, email); Invite / Re-issue invite / Deactivate actions with confirmation.
9. A2 Failed-push queue: table of failed pushes — operation, visa, attempts, last error, created, last tried; Retry per row and "Retry all"; empty state ("Nothing failed"). Include the "long-pending" variant rows with a distinct chip.
10. One "desktop states gallery" artboard: banners and chips as used on these screens.
