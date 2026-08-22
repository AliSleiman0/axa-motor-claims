# demo-fix-list.md — what the week-4 dry runs found

Slice 4.3. Every friction point from rehearsing `docs/demo-week4.md` end to end, triaged the way the
slice card requires: **before demo** (it breaks a beat — fixed in 4.3) or **7.2** (written down as a
ticket, not fixed now). Nothing here is a defect found in shipped behaviour unless it says so.

Dry run 1: 2026-08-22, beats 1-5 less the three permission-gated steps (notifications, location,
microphone were all `prompt` in the harness's Chrome profile).
Dry run 2: 2026-08-22, all five beats with the permissions granted. **Both push notifications real**
- the expert's assignment popup (confirmed on the desktop by screenshot as well as in the service
worker) and the garage's rejection popup, each carrying the right deep link; **Arrived against real
geolocation**; **a real `MediaRecorder` voice note** (384,764 bytes, `audio/webm`, stored
`not_applicable`, `sent` on attempt 1). Beat 3's corrected timing verified: restored inside the
40-second window, the queue drained in ~90 s with both rows recovering on attempt 2.

---

## Fixed before the demo

| # | Found | Why it breaks a beat | Fix |
|---|---|---|---|
| 1 | Beat 2's assignment injection was written as an inline `Invoke-RestMethod` referencing an `$adminToken` variable that exists nowhere. | The demo's opening move would not have run. | `scripts/demo-assign.ps1`, reusing the admin session `demo-reset.ps1` stores and refreshing it on 401 — so it still works at minute 20, and never trips the OTP resend throttle. |
| 2 | The pre-flight named only **notifications**. **Arrived** asks for location and the voice note asks for the microphone. | Two more Chrome permission bubbles, in front of the client, in the middle of beat 2. | Pre-flight item 2 now lists all three, per profile. |
| 3 | The doc said the confirm button was *"Use this photo"*. It is **Confirm**. | Improvisation in the first minute of the main beat. | Every label in the doc is now read off a real screen: *Confirm*, *Submit to AXA*, *Use this visa*, *Create declaration*. |
| 4 | The doc said the post-submit status reads *"submitted"* and the rejected one *"rejected"*. They read **Waiting for AXA** and **Not accepted**. | The presenter describes one thing while another is on screen. | Corrected, including the garage's rejection sentence verbatim. |
| 5 | **Beat 3's timing was wrong.** The doc promised a ~90 s drain. If a queued row fails a *second* time before the failure rate is restored, its next retry is **five minutes** out. Reproduced in dry run 1. | A five-minute silence in a fifteen-minute demo. | The beat now says to restore within ~40 s of the last upload, and carries an explicit recovery line: name the five-minute step, move to beat 4, show the drained queue afterwards. |
| 6 | `demo-outbox.ps1` printed a future `retry_at` on rows already `sent`. | It is the lease the worker pushed out when it claimed the row, but on a projector it reads as "this is going to be sent twice". | The column is now blank unless the row is `pending`/`processing`. |
| 7 | `demo-reset.ps1` died on `.Count` of an empty SMS list under `Set-StrictMode`. | The reset never completed. | Call sites wrapped in `@(...)`. |
| 8 | `demo-outbox.cs` failed the repo's analyzers (CA1305) — and the reset's warm-up swallowed the error. | The queue viewer would have failed for the first time during beat 2. | Culture-invariant conversion; and the warm-up now throws on a build failure while still tolerating "database not there yet". |
| 9 | `demo-assign.ps1` read `$status` from `-StatusCodeVariable` inside a function, where it is out of scope. | The command threw after successfully injecting — worst kind of failure to see live. | Returns status and body together. |
| 10 | Nothing generated demo media, and the clarity refusal needs a file that is genuinely blurry at 1600×1200. | Beat 2's refusal could not be shown without hand-made assets. | `scripts/demo-media.mjs`, run by the reset. Both images were checked against the real gate (`assessClarity`): sharp 49761, blurry 0.84, threshold 100. |
| 11 | **Sign-in is throttled for ~60 s immediately after `demo-reset.ps1`.** The reset activates each user by sending them an OTP, and `Auth:OtpResendSeconds` is 60, so the first sign-in attempt gets a 429 and no new code. Hit on the first move of dry run 2. | The presenter's first action after the reset fails, silently, with a stale code in the log that will not verify. | The reset's closing card says so in yellow, and the pre-flight orders the steps so the profile setup spends that minute. Not "fixed" in code: the throttle is a §9 control and shortening it for a demo would be changing a security setting to suit a rehearsal. |

## Carried to 7.2

| # | Item | Why not now |
|---|---|---|
| 12 | **The fake NEXT3's *Survey* / *Expert documents* folders are only observable through the outbox row's status.** `FakeNext3Client.RecordedDocuments` is `internal` — test observability. The demo asserts "it is filed in NEXT3" from a `sent` row rather than showing the receiving end. | Exposing the fake's contents is a small dev endpoint, but it is product surface built for a demo. A2 (slice 6.2) is the real answer for the queue, and #1's sandbox is the real answer for the folder. |
| 13 | **`demo-outbox.ps1` is a stand-in for A2.** It reads the database directly, which no shipped code path does. | A2 is slice 6.2 by plan. Recorded so the stand-in is not mistaken for a design. |
| 14 | **The demo cannot show two roles simultaneously in one browser** — one bearer token per origin. Three Chrome profiles is the workaround, and it costs four sign-ins in the pre-flight. | Real, but it is a demo-rig problem, not a product one: in life the garage and the officer are different people on different machines. |
| 15 | **Audio playback could not be verified in this Chrome profile** (carried from slice 3.1 — a known-good WAV stalls identically, so it is the profile, not the app). The voice-note beat proves recording, upload and storage; *hearing* it back is on the week-6 device checklist. | Needs a real handset, which week 6 has. |
| 16 | **A browser has one push subscription per profile, not per user — and the client treats it as proof the *current* user is subscribed.** `usePushSubscription` reads `pushManager.getSubscription()` on mount and, if the browser has one, sets `enabled` and shows *"Notifications are on"*. `enable()` then early-returns, so **no `POST /api/push/subscriptions` is ever made for the second user** and the server has no row for them. Confirmed in dry run 2: the garage signed in after the expert in the same profile, was told notifications were on, and `demo-outbox.ps1` showed `push ... failed ... The user has no active push subscription` with the fallback email going out instead; the `push_subscription` table held one row, the expert's. Unsubscribing in the browser and pressing the button again produced a proper garage row with its own endpoint, and the garage's rejection popup then arrived for real. **This is the exact case design.md §4 anticipated** when it scoped the unique index to `(user_id, endpoint_hash)` "so two people sharing a browser profile each keep their own row" — the schema allows for it and the client defeats it. It is the inverse of the bug 3.4 fixed: a screen saying notifications are on while they are off. | It breaks no demo beat, because the demo runs one Chrome profile per role, and no field user shares a device with another role. The fix is a behaviour change on mount (POST the existing subscription rather than only reading it, relying on the unique index as the upsert) in a component 5.2, 5.3 and 6.1 all reuse — that is feature work with its own tests, and 4.3 is a verification slice. Written up rather than smuggled in. |
| 17 | **A rejected declaration keeps its blobs for ever** (design.md §7.3, raised in 4.1). The demo's second declaration is now one such row. | Deleting a rejected claim's photographs needs a client answer (#4/#22). Already a 7.2 ticket; the demo just made it visible. |

## Deliberately not changed

- **G1's media count includes the officer's approval image** (3 where the garage uploaded 2). It is
  the declaration's media and the count is right; recorded in `scope-decisions.md`, and beat 4 now
  tells the presenter to say so rather than look puzzled. This closes the decision 4.2 left open.
- **Some button clicks needed a second press, or a JS-dispatched click, under browser automation.**
  Worst on **Arrived**, where the harness's synthetic mouse events never reached the button at all
  while a scripted `.click()` worked first time and recorded the arrival with real GPS. Never reproduced
  with a hand on the mouse, and the double press created **no duplicate rows** — the `inFlight` latch
  from 4.2 holding, which is worth more than the annoyance costs. Noted in the doc's recovery table
  so it is not mistaken for a bug if it appears.
