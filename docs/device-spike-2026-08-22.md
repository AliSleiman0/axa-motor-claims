# device-spike-2026-08-22.md — what two real handsets actually did

**Internal. Slice 6.3a.** The raw observation log behind `docs/research-capacitor.md`; that document
is the deliverable, this one is its evidence.

**Every row below is a fact somebody saw on a phone, with a screenshot.** Where a check was not run,
the row says so and says why. Nothing here is an expectation written up as a result — the whole point
of pulling this slice forward from week 6 is that HANDOFF §4 and design.md §7.1 currently rest on
prose, and §11 says there is no descope lever left if week 6 discovers they were wrong.

**Handsets:** Samsung (Android, USB debugging, Capacitor debug APK) · iPhone 17 Pro Max (Safari, then
the PWA installed to the Home Screen). Screenshots in `docs/device-spike-2026-08-22/`, named by check.

---

## The rig

| Piece | Value |
|---|---|
| API | `http://localhost:5180`, `Blob__Mode=azure`, `Push__Mode=webpush`, `Next3:Mode=fake` (`demo-reset.ps1`) |
| Vite (http) | `:5173` — the Samsung reaches it via `adb reverse tcp:5173 tcp:5173` |
| Vite (https) | `:5174` on `192.168.10.90` via `vite.config.spike.ts` — the iPhone, mkcert leaf, root installed as an iOS profile |
| Capacitor | 8.5.0 · `appId` `PLACEHOLDER.axa.motorclaims` · `server.url` `http://localhost:5173` |
| Android build | AGP 8.13.0, Gradle 8.14.3, JDK 21.0.12, compileSdk/targetSdk 36, minSdk 24 |
| APK | `app-debug.apk`, 4.3 MB, debug-signed |

**Why the Samsung goes through `adb reverse` rather than the LAN.** Chromium treats `http://localhost`
as a *potentially trustworthy origin*, so the WebView gets the secure context that `getUserMedia`,
geolocation and service workers all require — with no certificate and no CA on the handset, and no
machine-specific address in `capacitor.config.json`. Android's own cleartext policy still blocks it at
targetSdk 28+ with no localhost carve-out, which is what `app/src/debug/AndroidManifest.xml` is for;
that overlay is debug-only and merges into no release APK.

**Two shell changes were needed before any check could mean anything**, and both are recorded here
rather than discovered as failures. The generated manifest declares `INTERNET` and nothing else, so
without `ACCESS_*_LOCATION` the geolocation prompt never appears and without `RECORD_AUDIO`
`getUserMedia` is refused before `MediaRecorder` is reached. **A missing manifest line looks exactly
like a platform limitation**, and would have gone into `research-capacitor.md` as one.

---

## Android — the Capacitor shell

| # | Check | What was done | What to look for | Observed | Screenshot | Verdict | Feeds |
|---|---|---|---|---|---|---|---|
| A1 | Capture-only buckets open the camera with **no** gallery route | E2 → each of the 5 "Take a photo" inputs; then G2's Car photos | A camera, not a picker. Any "Gallery"/"Files" affordance is a failure. Note whether `capture="environment"` is honoured or `@capacitor/camera` is required | _pending_ | `a1-capture-only.png` | _pending_ | §7A Q4 · design.md §7.1's provisional clause |
| A2 | **Arrived** — geolocation | E2 → Arrived, granted; then revoke and repeat | Permission prompt, accuracy, seconds to fix; and that a denied fix **blocks** rather than sending a partial arrival | _pending_ | `a2-arrived.png` | _pending_ | §7A Q6 · §5.1 |
| A3 | `MediaRecorder` | E3 voice note → record → playback → upload | Which `audio/*` the WebView emits, whether playback works, whether the server accepts it against `Media:AudioContentTypes` | _pending_ | `a3-voice.png` | _pending_ | §7A Q5 · 3.1's unproven playback |
| A4 | Damage diagram PNG export | E3 → diagram → mark → confirm → upload | Marks land where tapped; the export uploads and reaches `sent` | _pending_ | `a4-diagram.png` | _pending_ | §5.1 · #11 |
| A5 | Layout on real glass | Every expert and garage screen, portrait | Nothing clipped at the right edge; 48 px targets reachable with a thumb | _pending_ | `a5-layout.png` | _pending_ | 4.4 found the header overflow in a *browser* |
| A6 | Clarity gate on a real photo | E2 → capture a real 12–48 MP shot | Seconds from shutter to confirm screen; the reported sharpness; the byte size against `Media:MaxFileMb` = 15 | _pending_ | `a6-clarity.png` | _pending_ | §7A Q9 · §7.2 · 2.5's fixed analysis scale |
| A7 | Web push does **not** fire in the WebView | Expert screen → the push panel | Whether the panel offers "Enable notifications" or the unsupported notice; and whether a real assignment produces nothing | _pending_ | `a7-push.png` | _pending_ | **The reason 6.3 exists** |

## iOS — Safari, then the installed PWA

| # | Check | What was done | What to look for | Observed | Screenshot | Verdict | Feeds |
|---|---|---|---|---|---|---|---|
| B0 | The mkcert leaf is trusted | Installed `rootCA.crt` as a profile, enabled full trust in Certificate Trust Settings, opened `https://192.168.10.90:5174` | Whether Safari accepts a leaf valid to **23 Nov 2028** (~27 months) — Apple's 398-day ceiling exempts user-installed roots, but that is the claim under test | **"iphone worked"** — padlock, no certificate warning | `b0-cert.png` | **PASS** | blocks every other B row |
| B1 | Safari's capture input | All four §7.1 buckets exercised, plus an upload into `expert_report` | Safari offers a sheet: record exactly what it lists. "Photo Library" appearing is §7.1 unenforceable in Safari | **"take a photo never asks for gallery, just take a photo option"** — a capture-only bucket offers **one** control and it goes straight to the camera; `insured_documents` shows **two**, *Take a photo* and *Choose a file*. **No Photo Library route from a capture-only bucket.** Server side: six documents, all `sent` on attempt 1 — `insured_car_photo` 3,307,493 b and `tp_car_photo` 3,306,658 b both `origin = captured` and `passed`; `insured_documents` 3,917,618 b and `tp_documents` 3,018,445 b likewise; `expert_report` took **`IMG_1145.png`, `origin = uploaded`, 242,541 b** from the library | `b1-capture.png` | **PASS** | §7A Q4 · §7.1 |
| B2 | `MediaRecorder` | E3 voice note → record → play back → upload | Supported at all; the type produced (`audio/mp4` expected); server acceptance | **`audio/webm`, 335,436 bytes, `voice-note-89a057f1.webm`, `clarity_result = not_applicable`, outbox `sent` on attempt 1** — not the `audio/mp4` the design anticipated (see below). **Playback was audible — confirmed by ear, which closes the gap slice 3.1 left open** | `b2-voice.png` | **PASS** | §7A Q5 · 3.1's deferred playback check |
| B3 | Arrived geolocation | E2 → Arrived | Prompt, accuracy, time to fix, denied path | **Fix was immediate — no perceptible wait.** `update_arrival` for `PLACEHOLDER-VISA-0001` reached `sent` on attempt 1. *The denied path was not exercised on iOS; it is covered by the jsdom tests from 2.4 but not on a handset* | `b3-arrived.png` | **PASS** | §7A Q6 |
| B4 | **Add to Home Screen → real iOS web push** | Share → Add to Home Screen; open the installed app; Enable notifications; `demo-assign.ps1` | The install looks like an app (icon, no Safari chrome); the permission prompt appears **only** in the installed app; a real notification arrives and opens E2. **Also record the negative: the same button in the Safari tab.** | **NOT DONE — `push_subscription` is empty (zero rows, zero insert statements in the API log), and no `POST /api/push/subscriptions` was ever received.** Whatever else happened, no browser completed a subscription, so no iOS push can have been delivered | `b4-ios-push.png` | **NOT RUN** | §7A Q2 — *"the single most important question"* · HANDOFF §4's whole argument |
| B5 | Diagram export | E3 → diagram → mark → confirm → upload | Parity with A4 | **`damage-diagram-53afb65d.png`, 91,591 bytes, `image/png`, `clarity_result = passed`, outbox `sent` on attempt 1** — a canvas export rasterised correctly on iOS and cleared §7.2's server-side floor | `b5-diagram.png` | **PASS (server-verified)** | §5.1 |
| B6 | Layout and targets | Every screen used during B1–B5, portrait | Parity with A5 | **Nothing clipped, nothing awkward to hit with a thumb.** Note this is an iPhone 17 Pro Max — a *wide* phone, so it is the easy case; 4.4's 390 px header overflow would not necessarily reproduce here. The narrow-glass test belongs to the Samsung | `b6-layout.png` | **PASS (wide device only)** | 4.4 |

---

## Notes and incidents

- **Toolchain was mostly already present.** JDK 17 (Temurin), the Android SDK with platforms 34/35/36,
  build-tools 35/36, cmdline-tools and accepted licences were all installed on this machine. Only
  JDK 21 (`Microsoft.OpenJDK.21`, 21.0.12) and `mkcert` (1.4.4) were added, both by winget. **First
  APK existed ~7 minutes after starting**, against the 45–90 minutes budgeted — so the hour of
  fighting-budget was spent on the phones rather than on downloads.
- **`eslint .` walked into the generated Android project and failed the build.** Gradle copies
  Capacitor's `native-bridge.js` into `app/build/intermediates/`, and it carries inline disable
  comments for rules this config does not register. `android` joins `dist` in `globalIgnores`; it
  cannot hide authored code, because `webDir` is `dist` and the native project only ever receives
  what Vite emits.
- **`@capacitor/cli` brings three moderate advisories** — `xcode` → `uuid <11.1.1`
  (GHSA-w5hq-g745-h8pq). A dev-only CLI chain that never enters the app bundle; `npm audit fix --force`
  would downgrade the CLI. Recorded, not fixed, and worth a line in the handover.
- **The HTTPS dev server silently downgraded itself to plain http, and that is the incident worth
  reading.** The first version enabled HTTPS from `SPIKE_HTTPS_CERT`/`SPIKE_HTTPS_KEY` env vars inside
  `vite.config.ts`. It worked — until `git checkout` touched that file, Vite restarted the server
  **in-process** (same pid, confirmed), re-evaluated the config *without* those variables, and came
  back on `http://localhost:5174` with no LAN binding and **no error anywhere**. The phone would have
  lost its secure context, and the camera, the voice note and web push would all have stopped at once
  — indistinguishable, from the handset, from the platform limitations this spike exists to measure.
  It is 2.5's PDF-in-an-`<img>` shape again: the failure is invisible to the thing doing the failing.
  Fixed by making the config a pure function of files on disk — a separate `vite.config.spike.ts`
  reading a descriptor the script writes — and by **throwing rather than falling back to http**, since
  there is no useful degraded mode for a server whose entire job is providing a secure context.
  **`vite.config.ts` is now byte-identical to its 4.4 state**, so `npm run dev` and `demo-reset.ps1`
  cannot be affected at all. Verified by reproducing the exact trigger: `touch vite.config.ts`, watch
  the restart in the log, confirm HTTPS still answers on both the root and the proxied `/api`.
- **The API is run from a published copy, not `dotnet run`.** `demo-reset.ps1` runs the API out of
  `src/Api/bin`, which locks `Api.exe` — so the Stop hook's `dotnet test` could not rebuild and failed
  on a file lock every turn. **A guard that always fails is a guard that has stopped meaning
  anything**, and it would have masked a genuine test failure later in the slice. `dotnet publish -o
  demo-artifacts/api-run` and running that exe leaves `src/Api/bin` free. The one thing this gives up
  is the demo's live reload of `appsettings.Placeholders.json` (content root moves), which beat 3 needs
  and this spike does not.
- **curl on Windows cannot verify the mkcert chain** — schannel reports "the revocation status is
  unknown" because a mkcert root carries no CRL or OCSP. `--ssl-revoke-best-effort` validates. This is
  a curl artifact, not a certificate defect, and iOS does not require revocation data for a
  user-installed root — but it is the kind of thing that reads as a broken certificate at 9am.

## §7.1's biggest hedge just got a better answer than the design expected

design.md §7.1 says, of capture-only enforcement: *"In the browser, `capture="environment"` on the
input is a hint, not a guarantee."* That hedge is a load-bearing part of HANDOFF §4's case for
Capacitor — one reason to wrap the app at all was to reach a native camera that can suppress gallery
access.

**On this iPhone the hint was honoured.** A capture-only bucket presents a single control, tapping it
goes straight to the camera, and **no Photo Library route is offered at any point**. The contrast is
visible in the same screen: `insured_documents` shows *two* controls, *Take a photo* and *Choose a
file*, and only the second reaches the library.

Two cautions before this is leaned on. It is **one handset on one iOS version**, and Safari's
behaviour here is not a specification guarantee — a future release could reintroduce the picker
without warning, which is precisely why `origin` is recorded on every row and why the server-side
rule is not going anywhere. And **the Android WebView is untested** — that is check A1, still pending
the Samsung, and Chromium's WebView is a different implementation making its own choice.

But taken with B4's outcome, this is the observation most likely to move the go/no-go, because it
removes one of the two things Capacitor was bought for on iOS.

## Every iOS camera capture is called `image.jpg`, and that lands on this project's core problem

Four separate captures — insured documents, insured car photo, TP documents, TP car photo — arrived
with the **identical** `file_name` of `image.jpg`. That is Safari's behaviour, not a bug here: the
browser names every `<input type="file" capture>` result the same way, and the library upload by
contrast carried its real name, `IMG_1145.png`.

**Why it matters more here than it would elsewhere.** design.md §4 added `document.file_name` in slice
4.1 precisely so a garage's `invoice.pdf` would not reach NEXT3's *Survey* folder as `019ab….pdf` —
"on precisely the linking step this project exists to get right". On iOS that column does its job and
still leaves four files called `image.jpg` under one visa. Nothing here is wrong, and nothing on our
side can fix it: the name is all the browser gives.

So it becomes a question for AXA rather than a defect: **does NEXT3 distinguish documents by name?**
If it does, iOS uploads collide. The `doc_type` code (#12) and the `clientRef` (#32) both already
distinguish them, which is the reason to believe this is survivable — but it is now a known,
device-confirmed property of the iOS path and belongs in the next client email beside #12, not
discovered during UAT.

## The iOS voice-note finding, stated carefully

design.md §7.2 and slice 3.1 both anticipate **`audio/mp4`** from Safari — `MediaRecorder` "emits
`audio/webm;codecs=opus` on Chrome and `audio/mp4` on Safari" is written into the design. **This
iPhone produced `audio/webm`.**

That is not a client bug: `recorder.ts` picks its format from `Media:AudioContentTypes` using
`MediaRecorder.isTypeSupported`, and `audio/webm` is first in that list — so Safari answering *yes* to
WebM is what selected it. And it is not a mislabel either, because the **server sniffs the container
and refuses bytes that are not what they were declared to be**; a 335 KB file stored as `audio/webm`
means the bytes really are WebM.

**Consequence: the design document's parenthetical about Safari is now out of date**, and the code was
right to derive the format from a capability check rather than from a platform assumption — 3.1's
"accept loosely, store canonically" earning its keep on a device nobody had run it on. design.md §7.2
should be corrected rather than quietly diverged from, which is the eighth time this project has hit
that pattern.

**Still unproven: that the recording is audible.** Slice 3.1 could not verify playback because that
Chrome profile decoded no audio at all, and it was explicitly deferred to the device checkpoint. The
bytes being a valid container is not the same fact as a person hearing the note.

## Gates

`npm run build`, `npm test` (33 files, **279 tests**) and `dotnet test` (**503**, 501 passed, 2
environment-skipped — Azurite and the NEXT3 sandbox) are all green and **no test moved**.
