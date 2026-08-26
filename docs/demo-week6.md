# demo-week6.md — the end-of-week-6 demo, beat by beat

**Internal.** Not client-facing. Written 2026-08-26, after slice 6.3.
**Audience:** the manager. **Length:** ~20 minutes of demo, then questions.
**Supersedes `docs/demo-week4.md`**, which is the week-4 payment-gate script and now shows less than
half of what exists. Keep that file: it is the record of what was demoed at the milestone.

**Read the three rules before anything else.**

1. **Nothing on screen is client data.** Every visa, plate, name, document-type code, insurance type
   and email recipient is an obvious placeholder. Beat 7 exists to say so out loud before anybody
   asks. If a value ever looks like something AXA might recognise, that is a bug, not a nice touch.
2. **Follow the beats.** Every improvisation is a route nobody has walked. Ten slices running, the
   manual pass has found something no automated test could — that is the point of a script.
3. **Say what is not there.** Beat 8 is not optional and not an apology: NEXT3 is still the fake,
   nothing is deployed, and all 49 questions are unanswered. A demo that lets those be discovered
   later is worse than one that names them.

---

## What is new since the week-4 demo

Week 4 showed the expert chain, the declaration state machine and the outbox. Since then:

| | |
|---|---|
| **Broker Option 1** (5.2) | six-field form, documents, routed email by insurance type, Resend |
| **Broker Option 2** (5.3, 6.1) | a link to a member of the public with **no account**, five mandatory car shots on a tap-to-pick car, back to the broker as *ready to send* |
| **Repair flow** (5.1) | G4's post-repair uploads and the terminal state |
| **A2 failed-push queue** (6.2) | the admin safety net, with a live count in the nav |
| **Android app** (6.3) | **the big one — a real APK on a real phone**, native camera, FCM push |

---

## Pre-flight — 20 minutes before, not 2

```powershell
.\scripts\demo-reset.ps1
```

Drops and recreates the database, migrates it, starts Azurite and the API (`Blob__Mode=azure`,
`Push__Mode=webpush`), seeds **four** users and two assignments through the admin API, generates the
demo media, and starts the web app. ~60 s, idempotent, safe to re-run mid-demo.

Then, by hand:

| # | Do | Why |
|---|---|---|
| 1 | **Four Chrome profiles**: **Expert** (narrowed to phone width), **Garage**, **Officer**, **Broker**. Admin can share the Officer window. | One session per role; the app is role-gated server-side and a shared profile signs the previous role out. |
| 2 | **Grant permissions now**, per profile, at `http://localhost:5173`: Expert — notifications, location, microphone. Garage — notifications. | A permission prompt mid-beat is the most common derailment. |
| 3 | Sign each profile in. Codes: `.\scripts\demo-otp.ps1 <phone>`. | — |
| 4 | In Expert and Garage press **Enable notifications** once and confirm it says notifications are on. | Beat 3's popup depends on it. |
| 5 | Second terminal ready. Editor open on `src\Api\appsettings.Placeholders.json`. | Beats 4 and 7. |
| 6 | Confirm `Fake:FailureRate` is `0.0` and `Next3:Mode` is `fake`. | The reset warns on the first and refuses to start on the second. |
| 7 | **If you are doing beat 6 (Android):** phone unlocked, USB debugging on, `adb devices` shows it, and the APK installed — see that beat for the two commands. Do it **before** the demo, not during. | It is a five-minute setup and it is the beat people remember. |

**The cast** (all `+999` — an unassigned country code, so no real handset can be dialled by accident):

| Role | Phone |
|---|---|
| Admin (seeded by deployment config, §5.4) | `+999000000001` |
| Expert | `+999000003001` |
| Garage | `+999000003002` |
| Claim officer | `+999000003003` |
| **Broker** (new — added to the rig 2026-08-26) | `+999000003004` |

> **The trap that locked out a previous pass:** `Fake:FailureRate` is **global**. Setting it to 1.0
> to break NEXT3 also makes the fake SMS sender throw, so nobody can get an OTP. **Sign everyone in
> first, then break NEXT3.** Beat 4 is ordered that way for this reason.

---

## Beat 1 — The problem, in one sentence (1 min) · no screen

Say it before anything is shown, because every beat afterwards is an answer to it:

> Today a garage or an expert emails photographs to AXA and somebody links them by hand to the right
> visa number. That linking is the whole job, and it is where things go missing.

---

## Beat 2 — The expert chain (5 min) · Expert profile

1. **My claims** — two assignments, newest first, with media counts.
2. Open `PLACEHOLDER-VISA-0001`. **Arrived** → the date, time and GPS go to NEXT3. Show the
   Arrival line filling in. Say: *the button can only be pressed once, and arrival without a
   location is never sent — the BRD asks for all three values.*
3. **Insured car photos** — point out there is **only "Take a photo"**, no file picker. That is the
   BRD's hard rule expressed as what exists on screen.
4. Take the **blurry** photo from `demo-artifacts\media`. The clarity gate refuses it and says why.
   **Nothing leaves the browser.** Then the sharp one — it passes with its dimensions and sharpness
   shown, and you confirm before it uploads.
5. **Voice note** and **damage diagram** — mark two panels, export. Both go down the same pipeline.
6. `.\scripts\demo-outbox.ps1` in the second terminal: every upload is a queued row, `sent` on
   attempt 1, under one visa number.

---

## Beat 3 — The popup (2 min) · Expert profile

Leave the Expert window in the background.

```powershell
.\scripts\demo-assign.ps1 -VisaNo 'PLACEHOLDER-VISA-0003' -Reference 'DEMO-LIVE-01'
```

The popup arrives. Click it — it deep-links to that claim, not to a list. Say: *this is the BRD's
primary requirement, and on Android it is a real push notification to a real phone — beat 6.*

---

## Beat 4 — Kill NEXT3 (3 min) · Expert + Admin

**Everyone is signed in by now. This is why.**

1. Set `Fake:FailureRate` to `1.0` and save. No restart — the placeholder file is `reloadOnChange`.
2. Upload another photo as the Expert. It uploads fine; the *push* to NEXT3 fails.
3. `.\scripts\demo-outbox.ps1` — the row is retrying with a backoff.
4. **Admin → Failed pushes.** The row is listed with its operation, visa, attempt count and last
   error. Press **Retry**.
5. Set the rate back to `0.0`. The queue drains; the count in the nav falls to zero and the screen
   shows its reassuring empty state with a timestamp.

The line worth saying: *NEXT3 being down never stops an expert working, and nothing is ever silently
lost — there is a screen whose whole job is to be there when something has gone wrong.*

---

## Beat 5 — Garage, officer, and the link that does the work (4 min) · Garage + Officer

1. **Garage** files a declaration: plate, a photo, submit. The officer is notified.
2. **Officer** opens the inbox → the declaration → searches the visa → **Approve**. Watch the
   button name its three steps: *Rendering*, *Uploading*, *Approving*.
3. Back in **Garage**: the claim detail is now unlocked and the officer's comments are visible.
4. `.\scripts\demo-outbox.ps1` — the garage's documents, held back until approval, are now queued
   under the visa the officer chose. **This is the sentence to land:** *nothing reaches NEXT3 under a
   visa number until a human has chosen it.*
5. Optional if time: **Start repairs → G4** post-repair uploads → terminal state.

---

## Beat 6 — The Android app (4 min) · the phone, on the table

**This is the beat people remember. Set it up before the demo.**

```powershell
$env:CAP_SERVER_URL = 'http://localhost:5173'
cd src\Web; npx cap sync android
cd android; $env:JAVA_HOME = (Get-Item 'C:\Program Files\Microsoft\jdk-21*').FullName
.\gradlew assembleDebug
adb reverse tcp:5173 tcp:5173; adb reverse tcp:5180 tcp:5180
adb install -r app\build\outputs\apk\debug\app-debug.apk
```

> `adb reverse` is what lets the phone reach the laptop, and **it dies silently whenever the cable is
> re-seated**. If the app shows a blank page, re-run both `adb reverse` lines before anything else —
> a dropped tunnel cost an hour during the device pass.

On the phone, signed in as the Expert:

1. Open a claim → **Take a photo** on a car-photo bucket. **The system camera opens. There is no
   route to the photo library at all.** Say what that means: *in a browser this is a hint the
   platform is free to ignore, and Android ignores it — you get the whole gallery, and somebody can
   attach a screenshot to a claim. In the app it is the camera or nothing.*
2. Take the photo. The same clarity gate runs on a real 12 MP camera photo, and it uploads to the
   same claim you have been looking at on the laptop. Show the media count go up in Chrome.
3. Lock the phone or send the app to the background. Then:
   ```powershell
   .\scripts\demo-assign.ps1 -VisaNo 'PLACEHOLDER-VISA-0004' -Reference 'DEMO-LIVE-02'
   ```
   **The notification arrives on the phone.** Tap it — the app opens on that claim.
4. If asked whether it survives the app being closed: yes, and it was tested that way — FCM wakes the
   app from dead. **If asked about battery savers, answer honestly:** with Android's battery saver on
   and the app not excluded from battery optimisation, a push was accepted by Google and never
   surfaced on the handset. That is a per-phone setting at enrolment, it is written up in
   `docs/oem-push-guidance.md`, and it is the single biggest delivery risk on Android.

---

## Beat 7 — Broker, and the customer who has no account (4 min) · Broker profile

1. **Option 1:** new request, six fields, attach a document, **Submit**. The recipient is shown
   **before** you press, resolved from the insurance type. Show the routed email in the API log.
   *Two insurance types go to two different desks, and that table is a placeholder until AXA gives us
   the real one.*
2. **Option 2:** create a customer link and copy it. Open it in a **private window** — this is the
   public page, no account, no login.
3. Fill the six fields, attach a document, then the car: **tap a side on the car diagram, photograph
   it, repeat for all five.** The same clarity gate. **Send.**
4. Back in the Broker window: the request is **Ready to send**, the five shots are shown by side, and
   **Send email** delivers it with every attachment named.

The line: *the customer never had an account, never saw a claim, and could not have reached anything
else — that page is the only unauthenticated surface in the system and it is deliberately the
smallest thing that could work.*

---

## Beat 8 — What is a placeholder, and what is not built (2 min) · say all of it

**Placeholders, on screen right now:** every visa and plate, insured names, the document-type codes,
the insurance types, the email recipients, the IRIS code. All in one file
(`appsettings.Placeholders.json`) so a real answer is a config change, not a code change. Open it.

**Not built, and say so plainly:**

- **NEXT3 is the fake, in every environment**, as it has been since week 1. The real client is
  written and unit-tested; it has never spoken to a real NEXT3 because **the sandbox does not exist
  yet (#1)**. If that stays true, UAT runs on the fake.
- **Nothing is deployed.** Everything here is a laptop. The two environments, the pipeline and the
  Azure resources are weeks 7–8.
- **All 49 open questions are unanswered.** Not one insurance type, recipient, NEXT3 field name or
  document code is real.
- **iOS ships as an installed web app, not a store app.** That is a deliberate recommendation
  (`research-capacitor.md` §11) that takes a multi-week Apple dependency off the critical path, and
  it is reversible if AXA wants MDM — at the cost of #30's lead time.
- **Huawei handsets launched since 2020 cannot receive push at all** — no Google Play Services. That
  needs a second integration nobody has budgeted, and it depends on the device mix (#28).

---

## If something goes wrong

| Symptom | Do |
|---|---|
| A screen is stale or a state looks wrong | `.\scripts\demo-reset.ps1` — 60 s, and it is idempotent. Re-sign-in is needed. |
| No OTP arrives / 429 | The resend throttle is 60 s. Wait it out; do not press again. |
| No popup in Chrome | Notifications were never granted for `http://localhost:5173` in that profile. Pre-flight step 2. |
| Nobody can sign in | `Fake:FailureRate` is not `0.0`. It breaks the SMS sender too. |
| The phone shows a blank page | `adb reverse` dropped. Re-run both lines. |
| The phone shows no notification | Check the API log says *"1 of 1 device tokens accepted"*. If it does, the phone is the problem — battery optimisation, beat 6 item 4. |

---

## When you are done

```powershell
.\scripts\demo-reset.ps1 -Stop
adb reverse --remove-all
```

Stops the API, Vite and Azurite, and drops the phone's tunnels. The database is left alone.

---

## What this demo deliberately does not show

- **The audit log**, which records who uploaded which photo and when. Worth mentioning in answer to
  any InfoSec question; not worth a beat.
- **The admin CRUD screens** beyond Failed pushes — they exist and they are dull.
- **The outbox retry schedule** in full. It takes 26 h 36 m to exhaust, which is a sentence, not a
  demo.
- **Rejection of a declaration.** It is terminal by design and the garage sees only the status; it
  will surprise people, so it belongs in the scope letter rather than in a live demo where it reads
  as a bug.
