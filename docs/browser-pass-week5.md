# browser-pass-week5.md — what a real Chrome found in slices 5.1 and 5.2

Run 2026-08-24, against `scripts/demo-reset.ps1` (Azurite, `Blob__Mode=azure`, `Push__Mode=webpush`,
real outbox worker). One Chrome profile, sequential sign-ins: admin → broker → garage → officer →
garage → officer.

**Why this pass exists.** Both week-5 cards carry a *manual Chrome pass* in their DoD and neither had
had one: 5.1's ran before the extension was connected, and 5.2's was substituted with an HTTP walk
(`scripts/walk-broker.ps1`). Seven of the last eight slices had their real defect found here and
nowhere else — CLAUDE.md makes that structural, since jsdom has no canvas by decision.

**Triage, as `demo-fix-list.md` does it:** *defect* (shipped behaviour is wrong), *polish* (it works,
it looks wrong), *ticket* (real, not this slice's to fix), *expected* (recorded so it is not
re-reported). **Nothing was fixed** — the 5.1+5.2 diff is unchanged, awaiting review.

---

## Defects in what week 5 shipped

| # | Found | Why it matters | Proposed fix |
|---|---|---|---|
| 1 ✅ **fixed in 6.1** | **B2's attached-document list renders *below* the Submit button, and both panels are headed "Documents".** DOM order measured: `H3 Details → H4 Documents (capture) → BUTTON Submit → H3 Documents (list)`. | A broker presses **Submit** before ever seeing what is attached — and the email is built from exactly that list. Two adjacent panels carrying the same heading is the lesser half. | In `BrokerRequestPage`, render `<Documents>` above the draft panel's Submit, and title the list something the capture panel is not ("Attached", or fold the list into the capture panel). **Done exactly that in 6.1**: `Documents` gained a `title` prop, `DraftPanel` renders it between the capture panel and Submit as **Attached**, and the order is pinned by a test using `compareDocumentPosition` — because the words were all present before the fix and in the wrong sequence. |
| 2 ✅ **fixed in 5.3** | **An Option 2 request nobody has touched is described as "filed" and offered a Send email button.** A `link_issued` request (every field an em dash) renders `OutcomePanel`'s not-yet-sent branch: *"This request is filed, but the email has not gone yet."* + **Send email**. Pressing it returns `409 not_submitted`, so the card then shows **two contradictory sentences at once** — that one, and *"This request is not waiting to be emailed."* | The screen offers an action that cannot work, on a request that is not filed, and then argues with itself. The server refuses it (5.2's own `Option == 1 && State == Submitted` guard), so nothing is sent — this is a screen defect, not a data one. | `OutcomePanel` branches only on `emailedAt`. It needs the Option 2 states: `link_issued` / `customer_in_progress` → the artboard's *"Nothing to review yet"*, `expired` → reissue. B4 proper is 5.3; the fallthrough is 5.2's. **Done exactly that in 5.3**: `OutcomePanel` switches on `request.state`, and `link_issued`/`customer_in_progress` render "Nothing to review yet" with **no send control at all**. Three tests pin it, including that the old sentence is absent. |
| 3 ✅ **fixed in 6.1** | **The provenance chip is stretched to the full row width** — measured **707 px** for the word `captured`, `display: flex`. | It reads as a coloured bar across the row rather than a chip. New in 5.2: this indicator slot has only ever held `PushIndicator`, whose `<span class="push-indicator">` carries its own width. | `align-self: start` / `width: fit-content` on `.chip`, or wrap the chip in the `DocumentRow` indicator slot. **`width: fit-content` on `.chip` in 6.1**, on the chip rather than on `.doc-row`, because the next column-flex parent would otherwise do it again. |
| 4 ✅ **fixed in 6.1** | **B1's "Email not yet sent" caption wraps around the Resend button** — it renders as `Resend  Email` then `not yet sent` on the line below. | It is the caption that explains why a Resend is being offered, on the one row where something is wrong. | The caption is an inline sibling of the button inside the `<td>`; make the cell a column flex or move the caption under the chip. **A new `.cell-stack` in 6.1** — column flex, `align-items: flex-start`, so the button keeps its own width. |
| 5 ✅ **caption fixed in 6.1; the gate is #48** | **B2's Submit has no document requirement** (`disabled={submit.pending}` only) while its caption promises *"the details and every document above"*. | A request can be emailed to AXA with nothing attached, under a sentence saying documents went with it. Whether Option 1 *requires* a document is a real question — the BRD does not say — but the caption and the control currently disagree. | Either gate Submit on ≥1 document (and say so), or change the caption to "the details and any documents above". Worth a scope-decision line either way. **The caption, in 6.1** — "the details and **any** documents above". Gating it would invent a rule the BRD does not state, so the sentence was made true rather than the behaviour made stricter; **#48** still asks AXA whether Option 1 requires one, and the scope-decision line is written. |
| 6 ✅ **fixed in 6.1** | **Once a request is sent, B1 offers no way to reach it.** A `submitted`-and-emailed or `sent` row has no action *and* no row navigation, so `/broker/{id}` — the only place the routed recipient is shown — is reachable by URL only. | The artboard's "one action per row, and only where there is one" is right about *actions*; it did not mean the row should be inert. "Which desk did that go to?" is the question B4's own note says this screen exists to answer. | Make the insured-name cell a link, as O1 does with the plate. **Done in 6.1**, on **every** row including one that has no name yet — an Option 2 request exists before anybody has typed anything, and an em dash is still the way in. |

---

## Real, but not week 5's to fix

| # | Found | Why it matters | Where it goes |
|---|---|---|---|
| 7 | **Office shells still overflow at 390 px.** Measured on the officer shell: header `scrollWidth` **562** inside a 390 px shell, phone number `display: block`, **Sign out's right edge at 562 against the header's 390 — clipped off**. | This is 4.4's finding, fixed only for `.app-shell--touch`; `ui.css` hides the phone on touch shells alone. **5.2 added a fourth office shell (broker)**, so the affected population grew this week. A broker on a narrow laptop window loses the only way out. | Ticket. The fix is one selector — the same rule without the `--touch` qualifier, or a `min-width` on the header's flexible half. |
| 8 | **"Notifications are on" with no server-side subscription — reproduced by a database reset, not just two users in one profile.** The garage screen said *"Notifications are on. AXA's decisions will pop up on this device."* while `push_subscription` held **zero rows** (`INSERT INTO [push_subscription]` count: 0). The approval popup never arrived; §8's email fallback carried it (`FAKE EMAIL to demo-garage@example.invalid [Declaration approved]`). | `demo-fix-list.md` #16 records this as *the second user to sign in on one profile*. The real trigger is broader: **the browser's subscription outlives the server's record of it** — a DB restore, a re-created user, or a VAPID rotation all produce it, and the user is told the opposite of the truth. | Extends ticket #16 (7.2). `usePushSubscription` should reconcile with the server rather than trust `getRegistration()`. |
| 9 ✅ **route fixed in 5.3; catch-all still open** | **`/p/{token}` renders a completely blank page.** B3 shows the broker `http://localhost:5173/p/{token}` to hand to a customer; there is no `/p/:token` route **and no catch-all** in `App.tsx`, so any unknown path paints bare canvas. | 5.3 builds the route — that part is expected. The gap is that there is no not-found route at all, and B3 is the first screen that *hands out* a URL. A customer opening it today sees nothing, not an explanation. | 5.3 for the route; a catch-all is its own small ticket. **5.3 built `/p/:token` and added `/public` to the Vite proxy** — without the proxy entry the page would have 404'd in dev and looked like the same blank screen. The **catch-all is still absent** and stays a 7.2 ticket. |
| 10 | **An aborted query logs an unhandled `SqlException` and answers 503.** Three `503 → 200` pairs seen on `/documents` reads; the API log carries one `An unhandled exception has occurred … Microsoft.Data.SqlClient.SqlException: A severe error occurred on the current command. Operation cancelled by user.`, stack naming `BrokerRequestEndpoints.cs:109` (B1's list). | Cause is React **StrictMode's double mount in dev** aborting the first fetch, so it is dev-mostly. But a cancelled request becoming an unhandled exception and a 503 is a production shape too (a user navigating away), and it puts `fail:`-level noise in the log that would mask a real fault. | Ticket (7.2). Treat `OperationCanceledException` at the request boundary as a client disconnect, not a 500/503. |

---

## Observations — deliberate or arguable, not defects

- **"What was sent" disappears in the terminal state.** `SubmittedDocuments` is mounted only by the
  submitted and repairs panels, so the moment a garage presses **Submit repair documents** its own
  record of what it sent vanishes. Arguably wrong on the screen a garage returns to; recorded rather
  than assumed.
- **Effective date renders `mm/dd/yyyy`.** `type="date"` follows the browser locale; the wire value is
  ISO, which is what matters. For a Middle East product a US-ordered date on screen is worth a
  thought (it is a browser setting, not a code one).
- **B3's artboard "Afterwards" card is not built.** Returning to `/broker/link` shows a fresh form,
  not the drawn State / Sent to / Created / Expires + *Create a new link* card. The Option 2 row's
  own detail page is where that information would live — see defect 2.
- **Admin's nav wraps to two lines** at this viewport and the Brokers row's Actions column is clipped
  behind a horizontal scrollbar. Slice 1.3/4.4's screens, unrelated to week 5.

---

## What the pass confirmed working

**Slice 5.1 — the DoD line, end to end.** Approve → **Start repairs** → three panels (**Repair photos
(0)**, **Discharge (0)**, **Invoice (0)**) with *Choose a file* on the last two **only**; **Submit
repair documents** disabled under *"Add at least one repair document before finishing this
declaration."* and enabled by **one** document of any kind; capture + upload → **"Queued, will
send"** → after the worker tick **"Sent to AXA"** on both rows **before anything else was pressed**;
Submit → **Repair documents sent**, *"This declaration is complete."*, *"Everything is with AXA under
PLACEHOLDER-VISA-0001…"*, four timestamps all populated (Submitted 6:12:14, Approved 6:15:17, Repairs
started 6:16:55, Documents sent 6:20:46), **zero controls and zero file inputs**. The officer then saw
all four documents on O2 with three previews rendering at **1600×1200** from `blob:` URLs.
**All four outbox rows `sent` on attempt 1 under `PLACEHOLDER-VISA-0001`.**

**Slice 5.2 — the DoD line, end to end.** A brand-new broker onboarded through A1 → invite → OTP and
**landed on `/broker`** with a live **Requests** tab and no push panel (5.2's `homePathFor` and shell
changes, visible). The form offered exactly the three server-served types, kept **Save details**
disabled until all six were filled and **with an amount at `0` or negative**, and showed **no currency
symbol** anywhere (#47). Submit named the routed recipient on screen and in the log, with **both
attachments by name and size** and all six fields. **Routing proved on two desks:** `MOTOR ALL RISK` →
`PLACEHOLDER-recipient-1`, `MOTOR TOTAL LOSS` → `PLACEHOLDER-recipient-2`. The failure path behaved as
§5.3 designs it — request **filed**, `emailed_at` null, a `failed` notification row, **Not sent yet**
on the detail and **Resend** on B1 — and Resend then delivered it. B3 showed the link **once** as
`/p/{token}`, copied it to the real clipboard (**Copied**), and disabled **Send by SMS** with its
reason on screen.

**The two things only a browser proves.** The **clarity gate ran for real**: the blurry gradient was
refused with *"This photo is too blurry…"*, **no confirm screen, no document row, and no POST left the
browser** (network panel unchanged); the sharp checkerboard passed at **`1600×1200, sharpness
30724`**. And the **kill-switch worked live**: setting `Broker:AllowUpload` to `false` removed
`broker_document-upload` **from the DOM** — gone, not disabled — with no restart, and restoring it
brought the control back.

**`PushTiming.Never`, end to end.** Both broker documents stored `push_status = n/a` with a **null
doc type**, and the outbox contained **four rows, all for the declaration, none for the broker**.

Console across every screen visited: **clean** — Vite HMR only, no errors or warnings.

---

## Limits of this pass

- **Capture-only enforcement is not testable here.** On desktop Chrome `capture="environment"` is a
  plain file picker, so what was proven is §7.1's *rendering* rule (a capture-only bucket renders one
  control, an upload-allowed bucket two). Android needs `@capacitor/camera` (6.3); iOS honours it.
- **The push popup could not be observed**, because the profile's subscription predates the recreated
  database (finding 8). The failure and its email fallback were observed instead.
- **390 px was measured, not viewed.** Chrome would not size the window below ~516 CSS px at this
  display's scaling, so the header was constrained to 390 px and measured directly — which is how
  4.4 found the original overflow.
- Some clicks needed a scripted `.click()` rather than a synthetic mouse event — the automation
  flakiness `demo-fix-list.md` already records. No duplicate rows resulted.

---

## Fixed since

**2026-08-24, slice 5.3** — findings **2** and **9** (the route half). Nothing else in this file was
touched: the other eight remain open and unfixed, five of them week 5's own (1, 3, 4, 5, 6; corrected from "four" by the PO verification, 2026-08-24).

**2026-08-25, slice 6.1** — findings **1, 3, 4, 6** in full, and **5** as far as it can be settled
without AXA (the caption; the submit gate waits on #48). That closes **every week-5 defect**. What
remains in this file is the four that were never week 5's: **7** (office shells overflow at 390 px),
**8** ("Notifications are on" with no server subscription), **9**'s missing catch-all route, and **10**
(an aborted query logging an unhandled `SqlException`) — all still on 7.2's buffer list.
