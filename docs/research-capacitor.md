# research-capacitor.md — PWA + Capacitor, validated or killed

**Internal engineering research. Slice 6.3a, 2026-08-23.** Answers HANDOFF §7A's ten questions and
ends with a go/no-go and a revised estimate for slice 6.3.

Sections marked **[observed]** come from two real handsets — a Samsung Galaxy S22 Ultra on Android 16
running the Capacitor shell, and an iPhone 17 Pro Max running the web app and then the installed PWA.
The evidence is `docs/device-spike-2026-08-22.md`, thirteen checks with screenshots. Sections marked
**[web]** are the ones observation cannot reach; they carry sources and dates.

**Why this exists.** HANDOFF §4 chose PWA + Capacitor over a bare PWA and over Flutter and recorded
the choice as **provisional pending this document**. design.md §7.1 leans on it in writing — native
capture-only enforcement "**provisional pending `research-capacitor.md`**" — and §11 states there is
**no remaining descope lever**, so a wrong answer discovered at the week-6 checkpoint has nowhere to
go. That is why the spike was pulled forward to week 4.

**The short version.** Capacitor is a **go**, but almost every specific reason §4 gave for it has
turned out to be wrong, and the two platforms have swapped roles. **Android needs the native shell and
iOS very nearly does not.**

---

## 1. Capacitor + React, current setup **[observed]**

**Capacitor 8.5.0** (`@capacitor/core`, `@capacitor/cli`, `@capacitor/android`), added to the existing
Vite/React project. Node ≥ 22 required; this machine runs 24.18.0.

- `src/Web/capacitor.config.json` — **JSON rather than TypeScript on purpose**: a `.ts` config falls
  inside `eslint .`'s glob and `tsc -b`'s reach, and a spike file must not be able to change the
  colour of `npm run build`.
- `src/Web/android/` — a standard Gradle project. AGP 8.13.0, Gradle 8.14.3, compileSdk/targetSdk 36,
  minSdk 24. It consumes `dist` and nothing else; `npx cap sync` copies the Vite output in.
- **Toolchain cost, measured:** this machine already had the Android SDK and JDK 17. Only **JDK 21**
  and **mkcert 1.4.4** were added, both by `winget`. **First debug APK existed about seven minutes
  after starting.** On a bare machine budget 45–90 minutes, nearly all downloading.

**Three traps worth carrying into 6.3, because each cost real time and each reads as something else:**

1. **The generated `AndroidManifest.xml` declares `INTERNET` and nothing else.** Without
   `ACCESS_*_LOCATION` the geolocation prompt never appears and without `RECORD_AUDIO` `getUserMedia`
   is refused before `MediaRecorder` is reached. **A missing manifest line looks exactly like a
   platform limitation** and would have been written up as one.
2. **`adb reverse` speaks IPv4; Vite binds `::1` only.** The shell's first load was
   `net::ERR_EMPTY_RESPONSE` — the socket opened and closed with no bytes, while the server answered
   `200` from the dev machine. Start it with `--host 127.0.0.1`. The symptom reads as "the Capacitor
   shell is broken".
3. **`viewport-fit=cover` breaks the layout.** Added to `index.html` for PWA polish, it put the header
   under the system status bar with **Sign out** colliding with the battery icons. Capacitor insets
   the WebView correctly by default; `viewport-fit=cover` opts out into edge-to-edge with no
   `env(safe-area-inset-*)` handling. Removed.

**The dev loop.** `server.url` points the WebView at a running dev server. Pointing it at a LAN
address over plain http costs the secure context that `getUserMedia`, geolocation and service workers
all require; over LAN https it needs a CA installed on the handset *and* a debug
`network_security_config.xml`, because Android 7+ ignores user CAs for app traffic. **`adb reverse
tcp:5173 tcp:5173` avoids both**: Chromium treats `http://localhost` as a potentially trustworthy
origin, so the WebView gets a full secure context with no certificate anywhere, and
`capacitor.config.json` carries no machine-specific address. Android's own cleartext policy still
blocks it at targetSdk 28+, which is one line in a **debug-only** manifest overlay.

## 2. iOS push **[observed + web]** — *§7A's "single most important question"*

**Web push from an installed PWA works. It was received on the handset, twice.**

That answers the technical half of the argument §4 built its whole recommendation on:

> *"iOS web push only works after that install… an iPhone user who never installs simply never gets
> claims. That's a functional failure of the primary requirement."*

The install works and the push arrives. **What a spike cannot answer is the human half** — whether
field users complete Add-to-Home-Screen — and one datum cuts against optimism: **the installed PWA has
its own storage context, so it did not inherit the Safari session and required a fresh phone-OTP
sign-in.** The flow is not three taps; it is three taps plus an SMS round trip. That argues for
onboarding people *into the installed app* rather than letting them start in Safari and migrate.

**A trap that would have shipped silently.** The first send returned **`403 Forbidden`** and delivered
nothing. The cause was `Push:Vapid:Subject` = `mailto:dev@example.invalid`: **Apple validates the VAPID
`sub` claim and requires a routable contact** — a real mailto domain or a valid `https:` URL — and
`.invalid` is an RFC 2606 reserved TLD that can never receive mail. Changing only that value produced
`1 of 1 subscriptions accepted`. **FCM does not check this**, which is why slice 3.4 proved the whole
push chain against real Chrome and saw nothing wrong. Android and desktop would have worked while
every iPhone silently received nothing. See §12's ticket list.

**Native APNs through Capacitor** [web] still needs, in order: a **paid Apple Developer Program
membership** (#30), an App ID with the Push capability, an **APNs `.p8` key**, the Push Notifications
and Background Modes capabilities in Xcode, and a physical device — the Simulator cannot receive push.
None of it can start until #30 is answered, and provisioning takes weeks in an organisation that does
not already have an account. Routing iOS push through **Firebase** (the common Capacitor path) also
puts a Google dependency in front of AXA notification delivery, which is a question for #21 rather than
a developer default.

## 3. Android push **[observed + web]**

**Web push does not work in the Capacitor WebView, confirmed.** The WebView exposes no `PushManager`,
so the app renders `PushUnsupportedNotice` — *"This browser cannot show claim notifications. New claims
will still appear in My claims."* Slice 3.4's graceful-degradation path is doing exactly its job, and
this is why 6.3 exists at all. **Android therefore needs FCM through the native layer**; there is no
web-push fallback the way there is on iOS.

FCM setup itself is routine [web]: a Firebase project, `google-services.json`, the Gradle plugin,
`@capacitor/push-notifications`.

**The real risk is the handsets, not FCM.** Aggressive OEM battery management on Samsung, Xiaomi, Vivo
and Oppo kills the process holding FCM's socket, and delivery degrades sharply when an app has not been
opened recently; Android 16's better Doze detection does not change this, because the brand-specific
settings dominate. Mitigations are an in-app `ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS` prompt
(permitted by Play policy) and per-device guidance of the kind dontkillmyapp.com catalogues.

**And one hard fact that belongs in front of AXA rather than in a footnote: Huawei devices launched
from 2020 onward ship without Google Play Services, so FCM does not work on them at all.** Delivery
needs Huawei Mobile Services Push Kit — a second push integration, a second developer account, a second
store. Huawei has meaningful share in MENA. **This is a direct dependency on #28 and it is the first
thing that could make the BRD's primary requirement undeliverable for part of the expert network**
through no fault of the design.

## 4. Camera: forcing capture, blocking gallery **[observed]** — *the finding that decides 6.3*

design.md §7.1 hedges: *"In the browser, `capture="environment"` on the input is a **hint, not a
guarantee**."* **The two platforms answer in opposite directions.**

| | capture-only bucket, "Take a photo" | |
|---|---|---|
| **iOS Safari / installed PWA** | goes straight to the camera; **no Photo Library route offered at all** | hint **honoured** |
| **Android WebView (Capacitor)** | opens `PhotopickerGetContentActivity` — the full gallery. **No `ACTION_IMAGE_CAPTURE` is fired** | hint **ignored** |

Verified twice on Android, the second time after confirming the button's position in a prior
screenshot. **The app's own rule is correct** — `a1-buckets-2.png` shows all five buckets, one control
on each capture-only bucket and two on each upload-allowed one. The platform simply ignores the
attribute.

**Why this is not cosmetic.** An expert or garage can attach any existing image — a screenshot, an old
photograph, a picture of a picture — to a bucket the BRD requires to be taken at the scene. The server
cannot tell: §7.1 already states `origin` is *"a claim the client makes"*, and the row is written
`origin = captured` because the web layer believes it captured it. **This is precisely the fraud
surface capture-only exists to close, and on Android it is currently open in the browser, in the PWA
and in the Capacitor shell alike.**

**So `@capacitor/camera` is required on Android and is not required on iOS** — close to the reverse of
what §4 assumed when it bought Capacitor primarily for the iPhone.

Two cautions. Safari's behaviour is **not a specification guarantee**; a future release could
reintroduce the picker, which is why `origin` stays on every row and the server-side rule stays. And
this is one handset on one iOS version.

## 5. Voice recording **[observed]**

**Both platforms produce `audio/webm`.** iOS: `voice-note-89a057f1.webm`, 335,436 bytes. Android:
`voice-note-dbff98fc.webm`, 39,119 bytes. Both `clarity_result = not_applicable`, both `sent` on
attempt 1, both rendering `<audio controls>` for §7.2 item 4's playback-confirm.

**design.md §7.2 is wrong and needs correcting.** It states `MediaRecorder` "emits
`audio/webm;codecs=opus` on Chrome and **`audio/mp4` on Safari**". It does not. `recorder.ts` picks its
format from `Media:AudioContentTypes` using `MediaRecorder.isTypeSupported`, so **asking the browser
rather than assuming the platform is the only reason this worked at all** — 3.1's "accept loosely,
store canonically" earning its keep on devices nobody had run it on.

**Playback was audible on iOS**, confirmed by ear. That closes the gap slice 3.1 explicitly deferred to
this checkpoint: that Chrome profile decoded no audio, so *hearing* a note had never been proven.

**No plugin is needed for voice on either platform** — the web API is sufficient. Android needs
`RECORD_AUDIO` in the manifest before `getUserMedia` will even be offered.

## 6. Geolocation for Arrived **[observed]**

**Works on both.** iOS: fix was immediate, `update_arrival` sent on attempt 1. Android: sent on attempt
1 once the phone's location services were on.

**The failure path is correct and was proven on real hardware**, which jsdom could not do: with
location off, the app reported that arrival was not sent, **wrote no `update_arrival` row, and
re-enabled the button** — design.md §5.1's "arrival without location is not sent" holding outside the
test suite for the first time.

**But the Android WebView reports "location services are switched off" as code 3, `TIMEOUT`**, not code
2, `POSITION_UNAVAILABLE`. `geolocation.ts` already branches all four codes with distinct copy, so the
expert was correctly told what the platform said and incorrectly told what to do — *"press Arrived
again once the device has a signal"*, which will never work. **No amount of correct branching fixes
this**: the platform reports the wrong code and a web page cannot read the system location setting.
**Native code can** — Play Services both detects it and shows the standard "turn on location?" dialog.
That is the **second** entry in the column marked *only the plugin can do this*.

## 7. Building iOS without a Mac **[web]**

**Codemagic's free tier stands as §4 recorded it** (re-checked 2026-08-23): **500 build minutes/month
on macOS M2**, reset on the 1st.

- **Personal accounts only** — free minutes are not available on a Team. So, exactly as §4 says, the
  **CI account stays in the developer's name while the store accounts are AXA's**. That split must be
  written into the handover or it becomes an orphaned dependency the day the developer leaves.
- **One parallel build.** At a fifteen-minute iOS build that is roughly **33 builds a month** — ample.
- Codemagic builds native iOS, Ionic and Capacitor projects, not only Flutter.

**Still unverified and still on the critical path: that *this* project actually builds there**, which
cannot be tested until #30 provides an Apple Developer account to sign against. §7A item 7 is
**de-risked, not eliminated** — unchanged from §4's own wording. **§11 below removes it from the
critical path a different way.**

## 8. Distribution: store vs MDM **[web]** — *AXA's call (#29)*

| | Public App Store / Play | Enterprise / MDM |
|---|---|---|
| Review cycles | days per release, plus rejection risk for an app the public cannot use | none |
| Who can install | anyone | enrolled devices only |
| Fit for a closed network of AXA experts and garages | poor — a public listing for a private tool | good |
| Needs from AXA | Apple Developer Program, Google Play account | an MDM tenant, enrolment, Apple Business Manager |
| Effect on the schedule | review latency lands in weeks 7–8, the tightest part of §11 | removes review from the critical path |

**Recommendation: MDM if AXA already runs one, public stores only if not.** The BRD describes a closed
network — an invitation link sent to a known mobile number — and §11 has no room for a store rejection
in week 8.

**One consequence worth stating for §11's recommendation:** an installed **PWA cannot be distributed
by MDM as an app**. If AXA requires MDM on iOS, the iOS Capacitor build stops being optional.

## 9. Client-side image quality check **[observed]** — *already built*

Answered ahead of the research: design.md §7.2's gate shipped in **slice 2.5** — resolution floor,
greyscale → Laplacian → variance against `Clarity.BlurVarianceThreshold`, measured at a fixed
`Clarity.BlurAnalysisMaxEdge` so the threshold means the same thing on a 12 MP and a 48 MP handset,
then a confirm screen. **Explicitly not ML**, per §7A.

**On the iPhone, four captures of 3.0–3.9 MB all cleared the gate and the 15 MB cap**, and the diagram
export rasterised and gated correctly on both devices. **The Android gate was not measured on a camera
photograph** and the log says so: A1 means a capture-only bucket opens the gallery there, so exercising
it would have meant browsing the developer's personal photo library. The gate is the same pure function
over a pixel buffer on both platforms.

## 10. Is Capacitor still the right pick in 2026? **[observed + web]**

**Yes, and more clearly than before — but for different reasons than §4 gave.**

- **A bare PWA is no longer disqualified on iOS.** §4 rejected it because iOS web push required the
  install; the install works and push arrives. On iOS a PWA would also *enforce capture-only*, which
  the Android WebView does not.
- **A bare PWA is disqualified on Android**, which is the reverse of §4's expectation: no `PushManager`
  in the WebView is irrelevant to a browser PWA, but **Chrome for Android ignores `capture` the same
  way the WebView does**, so the fraud surface stays open, and there is no way to prompt for the system
  location setting.
- **Flutter remains rejected.** Nothing found here is a Capacitor limitation; the two real gaps —
  gallery suppression and location settings — are exactly what a Capacitor plugin exists to close, at
  a few days rather than the 3–4 weeks §4 priced Flutter at. Flutter is **still deferred, not
  rejected**, and there is now no evidence calling for it.
- **React Native** would be a rewrite of a finished React codebase to solve two plugin-shaped problems.
  No.

### Also confirm (§7A)

- **PWA fallback quality for desktop roles** (officer, broker, admin): unaffected — they use the
  browser app, which is what every screen was built and demoed in. No change.
- **Offline behaviour "for free":** effectively none, and worth knowing. iOS has **no Background Sync,
  no Periodic Background Sync and no Background Fetch**, so a PWA cannot refresh or flush a queue while
  the user is away from it. Offline capture stays out of scope (#45); nothing here changes that, and
  nothing arrives free.
- **Is 1–1.5 weeks right?** For **Android only, yes** — see §12. For both platforms it is optimistic
  once #30's lead time is counted, which is the reason §11 splits them.

---

## 11. Go / no-go

**GO on PWA + Capacitor — with the platforms split, which is the real recommendation of this document.**

> **Ship the Android Capacitor app. Ship iOS as an installed PWA for now, and treat the iOS Capacitor
> build as a separate deliverable gated on #29 and #30.**

**Why Android must be native:**

1. **Capture-only is unenforceable in the WebView and in Chrome** (§4). That is the BRD's hard rule and
   a fraud surface, and it is open today.
2. **No `PushManager` in the WebView** (§3), so the BRD's primary trigger — *"a popup message will show
   on the expert mobile"* — has no web fallback on Android at all.
3. **The system location setting cannot be detected or prompted from the web** (§6).

**Why iOS need not be, yet:**

1. **Safari honours capture-only** (§4), so the fraud surface is closed there without a plugin.
2. **Web push works from the installed PWA** (§2) — observed, twice.
3. Native iOS push cannot start until **#30**, whose lead time is measured in weeks and which is
   outside the developer's control.

**What the split buys:** it takes the Apple Developer account, the APNs provisioning and the Codemagic
pipeline **off the critical path for week 6 and for UAT**, while closing the one genuine security hole
this spike found. Given §11 of design.md records that **there is no remaining descope lever**, moving a
multi-week external dependency off the critical path is worth more than store presence in week 8.

**What the split costs, stated plainly so AXA can overrule it:**

- **No iOS store or MDM presence.** If AXA requires MDM on iOS (#29), this recommendation is void and
  the iOS build becomes mandatory — with #30 on the critical path.
- **iOS users must complete Add-to-Home-Screen**, and it is *three taps plus a fresh OTP sign-in*
  (§2). This is a training and rollout cost, and it is the one part of §4's original objection that
  the spike could not dismiss.
- **No silent or background push on iOS** — irrelevant to the BRD, which asks for a visible popup, but
  it forecloses future features that would need it.
- **Safari could change.** The capture-only behaviour is not a guarantee.

**Flutter is not reconsidered.** The week-6 checkpoint's trigger condition — "if push or camera prove
inadequate" — has fired on Android camera, and the fix is a Capacitor plugin, not a different stack.

## 12. Revised estimate for slice 6.3

§4's figure was **1–1.5 weeks for both platforms**. Measured against what is actually left:

| Work | Estimate | Blocked on |
|---|---|---|
| `@capacitor/camera` behind `CapturePanel` for capture-only buckets, + tests | **1.5–2 d** | — |
| Android FCM: Firebase project, `google-services.json`, `@capacitor/push-notifications`, **a second `IPushSender` adapter** (native tokens are not web-push subscriptions) and token registration | **2–3 d** | — |
| Location-settings detection and prompt (§6) | **0.5 d** | — |
| Release signing, keystore, release build, `usesCleartextTraffic` off the release path | **0.5–1 d** | — |
| Battery-optimisation prompt + OEM guidance (§3) | **0.5 d** | — |
| **Android subtotal** | **5–7 d ≈ 1–1.5 weeks** | — |
| iOS Capacitor build: APNs key, capabilities, Codemagic pipeline, first signed build | **+3–5 d** | **#30**, weeks of lead time |
| Store / MDM packaging and submission | **+1–2 d**, plus review latency | **#29** |
| **Huawei / HMS Push Kit, if the network includes Huawei** | **+3–5 d**, not currently budgeted anywhere | **#28** |

**So §4's 1–1.5 weeks is right for Android alone and optimistic for everything.** The recommendation in
§11 is what keeps the original figure honest.

### Tickets this spike raises but deliberately did not build

The spike's rule was shell, dev config, manifest and documents only. Each of these is a code change:

1. **`PushOptionsValidator` must reject an unroutable VAPID subject** (§2). It already checks the
   public key to the character and does not look at the subject at all. Rejecting `.invalid`,
   `.example`, `.test` and `localhost`, and requiring `mailto:` or `https:`, turns a **silent
   iOS-only outage** into a refusal to boot.
2. **`Push:Vapid:Subject` cannot be an obviously-fake placeholder** the way the rest of Appendix A can.
   It must be valid *and* routable — an AXA contact address or the deployed origin. Appendix A should
   say so where the value sits, or the next person reaches for another `example.invalid`.
3. **Android timeout copy should point at the location setting** (§6).
4. **design.md §7.2's `audio/mp4` claim is wrong** and should be corrected to say the format is chosen
   by capability check (§5).
5. **`viewport-fit=cover` must not be reintroduced** until the layout handles `env(safe-area-inset-*)`
   (§1).

### Blocked on the client

| # | Question | What it decides here |
|---|---|---|
| **#28** | Device mix across the expert/garage network | Whether **Huawei/HMS** is a third push integration (§3) — currently budgeted nowhere |
| **#29** | Store vs MDM | §8's packaging, and **whether §11's platform split survives**: MDM on iOS makes the iOS build mandatory |
| **#30** | Apple Developer account | Everything iOS-native in §2 and §7. **Multi-week lead time — start now**, even under §11's recommendation, because it gates the option |

---

## Sources

- [Codemagic — Pricing](https://codemagic.io/pricing/) · [Codemagic Docs — Pricing](https://docs.codemagic.io/billing/pricing/) (re-checked 2026-08-23)
- [Capawesome — The Push Notifications Guide for Capacitor](https://capawesome.io/blog/the-push-notifications-guide-for-capacitor/)
- [Capacitor Documentation — Push Notifications (Firebase)](https://capacitorjs.com/docs/guides/push-notifications-firebase)
- [CleverTap — Why push notifications go undelivered on Android](https://clevertap.com/blog/why-push-notifications-go-undelivered-and-what-to-do-about-it/)
- [Pushwoosh — Android push notifications (2026)](https://www.pushwoosh.com/blog/android-push-notifications/)
- [web-push-php #406 — `web.push.apple.com` returns 403 `BadJwtToken`](https://github.com/web-push-libs/web-push-php/issues/406)
- [RFC 8292 — Voluntary Application Server Identification (VAPID)](https://www.rfc-editor.org/rfc/rfc8292.html)
- [MagicBell — PWA iOS limitations](https://www.magicbell.com/blog/pwa-ios-limitations-safari-support-complete-guide) · [MobiLoud — Do PWAs work on iOS?](https://www.mobiloud.com/blog/progressive-web-apps-ios) — *vendor sources; structural limits only. Their delivery-rate figures are not relied on here.*
