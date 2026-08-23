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
| A1 | Capture-only buckets open the camera with **no** gallery route | E2 -> **Insured car photos** -> *Take a photo*, with the button position confirmed from a prior screenshot | A camera, not a picker. Any "Gallery"/"Files" affordance is a failure | **FAILS. It opens the gallery.** `topResumedActivity = com.google.android.photopicker/PhotopickerGetContentActivity` - an `ACTION_GET_CONTENT` picker over the whole photo library. **No `ACTION_IMAGE_CAPTURE` intent is fired at all**, so the WebView ignores `capture="environment"` outright. Verified twice. The app's own §7.1 rule *is* right - the bucket renders one control, `Insured documents` renders two - but that control does the wrong thing | `a1-capture-only.png`, `a1-buckets-2.png`, `a1-before-tap.png` | **FAIL - `@capacitor/camera` is required on Android** | §7A Q4 · design.md §7.1's provisional clause |
| A2 | **Arrived** - geolocation | Pressed twice: once with the phone's location services **off**, once after the developer turned them on | Permission flow, accuracy, seconds to fix; and that a denied fix **blocks** rather than sending a partial arrival | **Both paths correct.** Off: *"Arrival was not sent. Finding a location took too long..."*, **no `update_arrival` row written, button re-enabled** - §5.1 holding outside jsdom for the first time. On: `update_arrival` for `PLACEHOLDER-VISA-0002` **sent on attempt 1**. But the message named the wrong cause - see the note below | `a2-arrived.png` | **PASS (both paths)** | §7A Q6 · §5.1 · 2.4 |
| A3 | `MediaRecorder` | E3 -> Record a voice note -> Stop -> Confirm | Which `audio/*` the WebView emits, whether playback works, whether the server accepts it | **`audio/webm`**, `voice-note-dbff98fc.webm`, 39,119 bytes, `clarity_result = not_applicable`, outbox **`sent` on attempt 1**. `getUserMedia` worked in the WebView once `RECORD_AUDIO` was in the manifest; the panel showed *Recording...* then rendered `<audio controls>` at **0:46** for §7.2 item 4's playback-confirm. **Same container as iOS** - so design.md's "`audio/mp4` on Safari" is wrong about both platforms | `a3-voice.png` | **PASS** | §7A Q5 |
| A4 | Damage diagram PNG export | E3 -> tapped Front bumper and Bonnet -> Use this diagram -> Confirm | Marks land where tapped; the export uploads and reaches `sent` | **Marks landed exactly where tapped**, *"Marked: Front bumper, Bonnet"*, rendered in 4.4's pale fill with the darker border. Export rasterised on the device and reached E4 **with no sharpness line** (correct - a diagram is `photographic: false`). `damage-diagram-5ba9e65b.png`, 93,926 bytes, `passed`, **`sent` on attempt 1**, and the panel count went to **(1)** | `a4-diagram.png` | **PASS** | §5.1 · #11 |
| A5 | Layout on real glass | E1, E2 and every bucket panel, portrait, 1080x2316 | Nothing clipped at the right edge; 48 px targets reachable with a thumb | **A defect, and it was mine.** The header rendered *under* the system status bar, with **Sign out** colliding with the battery and wifi icons. Cause: the `viewport-fit=cover` this slice added to `index.html`. Removing it fixed it completely - Capacitor insets the WebView correctly by default, and `viewport-fit=cover` opts out into edge-to-edge with no `env(safe-area-inset-*)` handling in the layout. **Removed.** Everything else is clean: no horizontal overflow, targets comfortably thumb-sized | `a5-layout.png` (broken), `a5-no-viewportfit.png` (fixed) | **PASS after fix** | 4.4 found its header overflow in a *browser* |
| A6 | Clarity gate on a real photo | **Not run on Android** | Seconds from shutter to confirm screen; the reported sharpness; the byte size against `Media:MaxFileMb` = 15 | **NOT RUN, deliberately.** A1 means a capture-only bucket opens the *gallery*, so exercising this on Android would have meant browsing the developer's personal photo library. Measured on iOS instead, where four captures of **3.0-3.9 MB** all cleared the gate and the 15 MB cap. The Android gate is the same pure function over a pixel buffer (`clarity.ts`), and the diagram export did rasterise and gate correctly on the device (A4) | - | **NOT RUN (iOS-measured)** | §7A Q9 · §7.2 |
| A7 | Web push does **not** fire in the WebView | Visible on every screen from the login page onward | Whether the panel offers "Enable notifications" or the unsupported notice | **Confirmed, and it needed no assignment to prove.** The WebView exposes no `PushManager`, so `PushUnsupportedNotice` renders in place of the enable button: *"This browser cannot show claim notifications. New claims will still appear in My claims."* The graceful-degradation path slice 3.4 built is doing exactly its job | `a0-shell-boot.png` | **CONFIRMED - no web push in the WebView** | **The reason 6.3 exists** |

## iOS — Safari, then the installed PWA

| # | Check | What was done | What to look for | Observed | Screenshot | Verdict | Feeds |
|---|---|---|---|---|---|---|---|
| B0 | The mkcert leaf is trusted | Installed `rootCA.crt` as a profile, enabled full trust in Certificate Trust Settings, opened `https://192.168.10.90:5174` | Whether Safari accepts a leaf valid to **23 Nov 2028** (~27 months) — Apple's 398-day ceiling exempts user-installed roots, but that is the claim under test | **"iphone worked"** — padlock, no certificate warning | `b0-cert.png` | **PASS** | blocks every other B row |
| B1 | Safari's capture input | All four §7.1 buckets exercised, plus an upload into `expert_report` | Safari offers a sheet: record exactly what it lists. "Photo Library" appearing is §7.1 unenforceable in Safari | **"take a photo never asks for gallery, just take a photo option"** — a capture-only bucket offers **one** control and it goes straight to the camera; `insured_documents` shows **two**, *Take a photo* and *Choose a file*. **No Photo Library route from a capture-only bucket.** Server side: six documents, all `sent` on attempt 1 — `insured_car_photo` 3,307,493 b and `tp_car_photo` 3,306,658 b both `origin = captured` and `passed`; `insured_documents` 3,917,618 b and `tp_documents` 3,018,445 b likewise; `expert_report` took **`IMG_1145.png`, `origin = uploaded`, 242,541 b** from the library | `b1-capture.png` | **PASS** | §7A Q4 · §7.1 |
| B2 | `MediaRecorder` | E3 voice note → record → play back → upload | Supported at all; the type produced (`audio/mp4` expected); server acceptance | **`audio/webm`, 335,436 bytes, `voice-note-89a057f1.webm`, `clarity_result = not_applicable`, outbox `sent` on attempt 1** — not the `audio/mp4` the design anticipated (see below). **Playback was audible — confirmed by ear, which closes the gap slice 3.1 left open** | `b2-voice.png` | **PASS** | §7A Q5 · 3.1's deferred playback check |
| B3 | Arrived geolocation | E2 → Arrived | Prompt, accuracy, time to fix, denied path | **Fix was immediate — no perceptible wait.** `update_arrival` for `PLACEHOLDER-VISA-0001` reached `sent` on attempt 1. *The denied path was not exercised on iOS; it is covered by the jsdom tests from 2.4 but not on a handset* | `b3-arrived.png` | **PASS** | §7A Q6 |
| B4 | **Add to Home Screen → real iOS web push** | Installed to the Home Screen, signed in again, enabled notifications, then three assignments | The install looks like an app; the permission prompt appears **only** in the installed app; a real notification arrives and opens E2 | **A real iOS web push was received on the handset — twice, confirmed by eye.** One `push_subscription` from `web.push.apple.com` (`p256dh` 87 chars = 65 bytes, `auth` 22 = 16, exactly as the endpoint requires). `DEMO-LIVE-01` **403, `0 of 1 accepted`, `notified_at` null**; after changing only `Push:Vapid:Subject`, `DEMO-LIVE-02` and `DEMO-LIVE-03` both **`1 of 1 accepted`** with `notified_at` set. The third was fired by the developer independently. **Slice 2.1's contract held on a real device: the refused assignment survived with `notified_at` null.** *The Safari-tab negative was not reported, and the notification was not tapped through to E2* | `b4-ios-push.png` | **PASS** | §7A Q2 — *"the single most important question"* · HANDOFF §4's whole argument |
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

## The Android WebView reports "location is switched off" as a *timeout*, and that misleads the expert

The handset's system location toggle was off. `geolocation.ts` already branches on all four
`GeolocationPositionError` codes with distinct copy - denied, unavailable, timeout, unsupported - so
this should have been the `unavailable` case: *"This device could not determine a location."*

**It came back as code 3, TIMEOUT**, after the full 15-second wait, so the expert was told *"Finding a
location took too long... press Arrived again once the device has a signal"* - advice that will never
work, because no amount of signal helps when location services are off. The developer found the real
cause by turning the toggle on, at which point the arrival sent on attempt 1.

**No amount of correct branching fixes this**: the platform reports the wrong code, and a web page
cannot read the system location setting. What *can* fix it is native code - Play Services can both
detect the setting and show the standard "turn on location?" dialog - which is another entry in the
column marked "things only the Capacitor plugin can do", alongside A1.

**Two honest corrections to "the app should have asked me":** the app-level permission prompt did not
appear because **this session granted `ACCESS_FINE_LOCATION` over `adb` before testing**, to keep A2
about the app rather than about a system dialog; a real install would prompt. And the *system* toggle
is a separate thing that no web page can prompt for at all. Carried as a **6.3 ticket**: on Android,
the timeout copy should also point at the location setting.

## The two platforms answer §7.1 in opposite directions, and that decides slice 6.3

design.md §7.1 hedges capture-only enforcement in the browser as *"a hint, not a guarantee"*, and
makes native enforcement **provisional pending this document**. The two handsets disagree, and the
disagreement is the whole answer:

| | capture-only bucket, "Take a photo" | verdict |
|---|---|---|
| **iOS Safari / PWA** | goes straight to the camera; no Photo Library route offered | the hint **is** honoured |
| **Android WebView (Capacitor)** | opens `PhotopickerGetContentActivity` - the full gallery. No `ACTION_IMAGE_CAPTURE` is fired at all | the hint is **ignored** |

**So `@capacitor/camera` is required on Android and is not required on iOS**, which is close to the
reverse of what HANDOFF §4 assumed when it bought Capacitor primarily for the iPhone.

**How bad is the Android hole?** A garage or expert can attach any existing image - a screenshot, an
old photograph, a picture of a picture - to a bucket the BRD requires to be taken at the scene. The
server cannot tell: §7.1 already says `origin` is *"a claim the client makes"*, and the row would be
written `origin = captured` because the web layer believes it captured it. **This is exactly the fraud
surface the capture-only rule exists to close**, and on Android it is currently open in the browser,
in the PWA, and in the Capacitor shell alike.

**It is not a regression and nothing here is wrong.** The app renders §7.1 correctly - one control on
a capture-only bucket, two on an upload-allowed one, confirmed in `a1-buckets-2.png` across all five
buckets. The platform simply does not honour the attribute. Closing it needs the plugin, which is a
component change and therefore **6.3's work, not this spike's**.

## `adb reverse` speaks IPv4 and Vite listens on IPv6, which looks exactly like a broken app

The shell's first load was `net::ERR_EMPTY_RESPONSE`. Vite's default `host: 'localhost'` binds
**`::1` only** on Node/Windows, `adb reverse` forwards the handset's localhost to the host's **IPv4**
`127.0.0.1`, and nothing was listening there - so the socket opened and closed with no bytes. The
server was healthy the whole time and answered `200` from the dev machine.

Fixed by starting that server with `--host 127.0.0.1`. Worth writing down because the symptom is a
blank app with a Chromium error page, which reads as "the Capacitor shell is broken" rather than "a
loopback family mismatch", and because `demo-reset.ps1` starts that server the default way.

## B4 is the headline, and it weakens the case for Capacitor on iOS

HANDOFF §4 rejected a bare PWA on one specific argument, quoted in full because everything now turns
on it:

> *"On **iOS**: Add-to-Home-Screen is a manual Safari-only three-tap flow that non-technical field
> users will not reliably complete, **and iOS web push only works after that install**. The BRD's
> entire expert flow is triggered by 'a popup message will show on the expert mobile' — so an iPhone
> user who never installs simply never gets claims. That's a functional failure of the primary
> requirement."*

**The technical half of that is now answered: the install works, and web push arrives.** A real
notification reached a real iPhone from this application server, twice.

**The human half is not answered and a spike cannot answer it.** Whether field users complete the
install is a rollout and training question, and one datum from this session is worth recording on the
sceptical side: **the installed PWA has its own storage context, so the Home-Screen app did not
inherit the Safari session and required a fresh phone-OTP sign-in.** The flow is not three taps; it is
three taps plus an SMS round trip. That is a real adoption cost, and it argues for onboarding people
*into the installed app* from the start rather than letting them use Safari and migrate later.

**Taken with B1, both of the reasons Capacitor was bought for iOS are now weaker than when §4 was
written** — Safari honours capture-only, and Safari delivers push. What Capacitor still buys on iOS is
store/MDM presence (§8), silent/background push, and insulation from Safari changing its mind. Whether
that is worth the Apple Developer account (#30), the Codemagic pipeline and the signing work is the
go/no-go, and it is written in `research-capacitor.md` §11 — **after** the Android half, because
Chromium's WebView is a different implementation and A1 and A7 are still unrun.

## The placeholder that works on Chrome and silently breaks iOS

**`Push:Vapid:Subject` was `mailto:dev@example.invalid`. Apple answered `403 Forbidden` and delivered
nothing. Changing only that value to a valid `https:` URL — same key pair, same subscription, same
code — produced `1 of 1 subscriptions accepted` on the next send.**

Apple validates the VAPID JWT's `sub` claim and requires a *routable* contact: a real mailto domain or
a valid URL. `.invalid` is an RFC 2606 reserved TLD that can never receive mail, so the token is
refused as `BadJwtToken`. **FCM does not check this** — slice 3.4 proved the whole push chain against
real Chrome with this exact subject and saw nothing wrong.

**This is a direct collision between two rules this project follows, and it is worth stating plainly.**
CLAUDE.md's placeholder discipline says client-specific values live in Appendix A as *obviously fake*
placeholders, and Appendix A duly carries
`"Subject": "mailto:PLACEHOLDER-push-contact@example.invalid"`. That value is obviously fake exactly as
intended — and it is also, on iOS only, a silent production outage. The failure mode is the worst
shape available: Android and desktop work, iPhones receive nothing, and the only symptom is a `failed`
row in the `notification` log that nobody reads until an expert says they never got a claim.

**What follows from it:**

1. `Push:Vapid:Subject` cannot be an obviously-fake value the way the others can. It has to be
   syntactically valid *and* routable — an AXA contact address, or the deployed application's own
   `https://` origin. The placeholder should say so where it sits, because the next person to fill it
   in will otherwise reach for another `example.invalid`.
2. **`PushOptionsValidator` already checks the VAPID public key to the character** (slice 3.4) and does
   not check the subject at all. Validating it — reject `.invalid`/`.example`/`.test`/`localhost`,
   require `mailto:` or `https:` — turns a silent iOS-only outage into a refusal to boot. That is a
   code change and therefore **out of scope for this spike**; it is written up here as a **6.3 ticket**.
3. It belongs in the runbook and in the handover: rotating VAPID keys already invalidates every
   subscription (§4), and now the subject has a correctness requirement of its own.

**None of this would have been found without a real iPhone.** Chrome was green on it, twice, in two
separate slices.

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
