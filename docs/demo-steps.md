# demo-steps.md — the week-4 demo, condensed for the presenter

**Internal.** The short, run-it-yourself version of `docs/demo-week4.md` (which carries the "say" column, the dry-run findings and the reasoning). Same beats, same labels, same files. Written 2026-08-23.

## Who's who

| Role | Who they are | What they do in the app |
|---|---|---|
| **Admin** | AXA's application administrator | Creates the other four profile types and sends invites. Nobody self-registers. |
| **Expert** | AXA's field assessor, dispatched to the accident site | Gets a push popup when NEXT3 assigns a claim, presses Arrived at the scene, captures photos / voice / diagram, uploads the report. Everything auto-files into NEXT3 under the visa. |
| **Garage** | A workshop in AXA's network where a customer brings the car instead of calling an expert | Declares a claim (plate + photos + survey documents) and submits it to AXA. Sees no claim details until AXA approves. Repair uploads come in week 5. |
| **Claim Officer** | AXA staff at a desk | Reviews garage declarations, finds the matching visa in NEXT3, approves (with comments) or rejects. Approval is what links the garage's photos to a visa and pushes them to NEXT3. |
| **Broker** | Sells new policies — unrelated to claims | Not in this demo (week 5). |

A **visa** is NEXT3's ID for a claim; the app never creates one. Expert path: the call centre opened the visa in NEXT3 before dispatching — the app just receives the assignment. Garage path: there is no visa until an officer finds (or creates, in NEXT3) one at approval. One claim is ever one path; only the garage path has a review step in the app; a submitted declaration goes to **all** officers, and whoever decides it first decides it.

## Setup — 15 minutes before (not 2)

1. In the repo: `.\scripts\demo-reset.ps1` — ~20 s; drops/recreates the DB, starts Azurite + API + web, seeds users and two assignments, generates demo media into `demo-artifacts\media\`.
2. Open **three Chrome profiles** — Expert (window narrowed to phone width), Garage, Officer (also used as Admin). One role per profile is mandatory: one token and one push subscription per profile.
3. In each profile visit `http://localhost:5173` and grant permissions now: Expert → notifications, location, microphone; Garage → notifications; Officer → none.
4. Wait ~60 s after the reset (OTP throttle), then sign each in at `/login`. Phones: Expert `+999000003001`, Garage `+999000003002`, Officer `+999000003003`, Admin `+999000000001`. Get each code with `.\scripts\demo-otp.ps1 <phone>`.
5. Expert and Garage: click **Enable notifications** once; confirm it says on.
6. Second terminal: run `.\scripts\demo-outbox.ps1` once. Editor open on `src\Api\appsettings.Placeholders.json`; confirm `Fake:FailureRate` is `0.0`.

## Beat 1 — Onboarding (Admin, ~2 min)

Sign in as admin → click through Experts / Garages / Claim officers / Brokers → **New** on Experts: phone `+999000009001`, name `DEMO Second Expert`, email `demo-expert-2@example.invalid` → **Create and send invite**. Row appears as *invited*. Terminal: `.\scripts\demo-otp.ps1 +999000009001` shows the invite SMS in the log (no SMS gateway yet — question #7). Don't register the new user; the point is who creates whom.

## Beat 2 — The expert chain (Expert, ~5 min)

1. Terminal: `.\scripts\demo-assign.ps1` → **a real Chrome notification** appears outside the window. That's the BRD's primary trigger.
2. Click it → claim `PLACEHOLDER-VISA-0003` opens with visa, policy, plate, insured, phone, vehicle, city, date — cached from NEXT3.
3. Press **Arrived** → becomes *Arrived at <time>*. Once-only, with GPS.
4. **Insured car photos → Take a photo** → `sharp-car-photo.png` → confirm screen shows "1600×1200, sharpness 30724" → **Confirm**. Note: this panel has no *Choose a file* — car photos are capture-only.
5. Same panel → `blurry-car-photo.png` → **refused outright** with the "too blurry… take it again" message. Nothing was sent.
6. **Insured documents → Choose a file** → `insured-document.pdf` → "Ready to send" (no sharpness — a PDF has no focus) → Confirm.
7. **Voice note → Record** 3 s → Stop → play back → Confirm.
8. **Damage diagram** → tap *Front bumper* and *Front left wing* → **Use this diagram** → Confirm.
9. **Expert report → Choose a file** → `expert-report.pdf` → Confirm.
10. Terminal: `.\scripts\demo-outbox.ps1` → five rows all `sent`, attempts 1: one `update_arrival`, four `upload_document`, all under VISA-0003. That table is "where is that photograph?" answered.

## Beat 3 — Kill NEXT3 (Expert + editor, ~3 min) — watch the clock

1. In the editor set `"FailureRate": 1.0`, save. No restart needed.
2. **Third-party car photos** → `sharp-car-photo.png` → Confirm. Do it twice. Nothing on the expert's screen changes.
3. `demo-outbox.ps1` → two rows `pending`, retry ~1 min out, error naming the injected failure.
4. **Within ~40 s of that last upload**, set `"FailureRate": 0.0`, save.
5. Wait up to ~90 s (worker polls every 30 s) → `demo-outbox.ps1` → everything `sent`, the recovered rows at attempts 2. Nobody re-uploaded anything.

If a row shows attempts 2 and isn't sent, its next retry is five minutes out — that's the backoff working; move to beat 4 and check again after. Don't sign anyone in while the rate is 1.0 (the fake SMS fails too).

## Beat 4 — The declaration (Garage ↔ Officer, ~4 min)

1. **Garage → New declaration**, plate `PLC-TEST-04` → **Create declaration** → status *Draft*.
2. **Car photos** (no file picker) → `sharp-car-photo.png` → Confirm. **Survey documents → Choose a file** → `garage-invoice.pdf` → Confirm. *(Optional: `demo-outbox.ps1` shows both as `deferred` — nothing goes to NEXT3 until there's a visa.)*
3. **Submit to AXA** → *Waiting for AXA*.
4. **Officer:** notification arrives → **Declarations to review** → open it. Photo inline, PDF as a named link, garage contact shown.
5. **Find the claim in NEXT3:** plate `PLC-TEST-04` → **Search** → **Use this visa** → `PLACEHOLDER-VISA-0004` fills in. Type a comment → **Approve**. (Browser renders the decision image, uploads it, then approves — three steps, that order.)
6. `demo-outbox.ps1` → three new rows under VISA-0004, `sent`: two `upload_document` + one `push_approval`, in the *Survey* folder, original filenames kept.
7. **Garage:** popup arrives → status *Approved*, full claim details unlocked, AXA comments, **Start repairs** button.
8. Rejection path: Garage → new declaration `PLC-TEST-05`, one car photo, Submit. Officer → open, comment `PLACEHOLDER - duplicate of an existing claim.`, **Reject**. Garage sees *Not accepted* — status only, no comment, no visa, terminal. That's by rule (BRD grants comments on confirmation only).

The approved declaration shows **Media 3** where the garage uploaded two — the third is the officer's approval image. Correct, not a bug.

## Beat 5 — Placeholders (~1.5 min)

Put `appsettings.Placeholders.json` on screen: `PLACEHOLDER-DOC-01…` are NEXT3 document codes (#12), the insurance types (#14), the NEXT3 URL (#1), `+999` phones, `.invalid` emails (#13). All fake on purpose; each answer is a config change, not a rebuild. And everything ran against the simulator — every day the sandbox slips, the date slips with it.

## If something breaks

No notification → check the site permission and that Enable notifications was pressed (the email fallback still fires). Queue not draining → confirm rate is 0.0, wait 30 s. Anything else → `.\scripts\demo-reset.ps1` (20 s, clean start).

## Afterwards

`.\scripts\demo-reset.ps1 -Stop` — otherwise the running API locks `Api.exe` and the next `dotnet build` fails with a misleading MSB3027.

Not shown, say so if asked: the failed-push admin screen (week 6), repair uploads (week 5), both broker options (weeks 5–6), the mobile wrapper, and any real NEXT3.

## Running the demo alongside week-5 coding (added 2026-08-24)

The demo runs from a **frozen worktree** so `main` can keep moving:

- Worktree: `C:\dev\axa-demo` on branch `demo-week4` (created at the rehearsed commit). Run `demo-reset.ps1`, the rig and the beats **from there** — it has its own `bin\` (no `Api.exe` lock against builds on `main`) and its own `demo-artifacts\`.
- User-secrets (the VAPID pair) carry over automatically — they are keyed by the project's `UserSecretsId`, not the folder.
- The one shared resource is **ports 5180/5173 and the `AxaMotorClaims` database**: while the demo rig is up, sessions on `main` may build and run `dotnet test` / `npm test` freely (tests use throwaway databases), but must not start the API/Vite or run a manual browser pass.
- Never merge week-5 work into `demo-week4`. If the demo date slips past a rehearsed change you *want* shown, re-freeze deliberately: move the branch, then **re-run one full dry run** — rehearsal is where beats break.
- After the demo: `git worktree remove ..\axa-demo` and `git branch -d demo-week4`.
