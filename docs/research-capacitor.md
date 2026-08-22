# research-capacitor.md — PWA + Capacitor, validated or killed

**Internal engineering research. Slice 6.3a, 2026-08-23.** Answers HANDOFF §7A's ten questions and
ends with a go/no-go and a revised estimate for slice 6.3.

> **STATUS: IN PROGRESS.** Sections marked **[observed]** are answered from the two handsets and their
> evidence is `docs/device-spike-2026-08-22.md`. Sections marked **[web]** are the ones observation
> cannot reach — they carry sources and dates. Sections still marked *pending* are waiting on the
> hand-tests; **the go/no-go at the end is not written until they are in**, because writing it first
> is how a spike becomes a document that confirms what somebody already believed.

**Why this exists and why now.** HANDOFF §4 chose PWA + Capacitor over a bare PWA and over Flutter,
and recorded the choice as **provisional pending this document**. design.md §7.1 leans on it in
writing — capture-only enforcement "in the Capacitor shell … *provisional pending
`research-capacitor.md`*" — and §11 states there is **no remaining descope lever**. So a wrong answer
discovered at the week-6 checkpoint has nowhere to go, which is the whole argument for pulling the
spike forward to week 4.

---

## 1. Capacitor + React, current setup **[observed]**

**Version 8.5.0** (`@capacitor/core`, `@capacitor/cli`, `@capacitor/android`), installed into the
existing Vite/React project on 2026-08-23. Node ≥ 22 required; this machine runs 24.18.0.

Structure, as actually generated:

- `src/Web/capacitor.config.json` — `appId`, `appName`, `webDir: "dist"`, `server`. **JSON rather than
  TypeScript on purpose:** a `capacitor.config.ts` falls inside `eslint .`'s glob and `tsc -b`'s
  reach, and a spike file must not be able to change the colour of `npm run build`.
- `src/Web/android/` — a standard Gradle project. AGP 8.13.0, Gradle 8.14.3, compileSdk/targetSdk 36,
  minSdk 24. It consumes `dist` and nothing else: `cap sync` copies the Vite output into
  `app/src/main/assets/public`.
- **The web build feeds the shell by copy, not by reference.** `npx cap sync` after every `npm run
  build`; there is no live binding.

**The dev loop is the part worth recording, because the obvious version does not work.** Capacitor's
`server.url` points the WebView at a running dev server, which is what gives live reload on the
handset. Pointing it at a LAN address over plain http costs you the secure context — and
`getUserMedia`, geolocation and service workers all require one, so the voice note, Arrived and push
would all fail for a reason that has nothing to do with Capacitor. Pointing it at LAN **https** works
but drags in a certificate the WebView will not trust without installing a CA on the handset *and* a
debug `network_security_config.xml`, because Android 7+ ignores user CAs for app traffic.

The cheap route is neither: **`adb reverse tcp:5173 tcp:5173`** maps the handset's own `localhost` to
the dev machine's port. Chromium treats `http://localhost` as a *potentially trustworthy origin*, so
the WebView gets a full secure context with **no certificate and no CA on the phone**, and
`capacitor.config.json` carries no machine-specific address. The one thing it does not get past is
Android's cleartext policy (targetSdk 28+ blocks it, with no localhost carve-out), which is one line
in a **debug-only** manifest overlay that merges into no release APK.

**Toolchain cost, measured:** this machine already had the Android SDK (platforms 34/35/36,
build-tools 35/36, cmdline-tools, licences accepted) and JDK 17. Only **JDK 21** (Microsoft OpenJDK
21.0.12) and **mkcert 1.4.4** were added, both by `winget`. **First debug APK existed about seven
minutes after starting.** On a bare machine, budget 45–90 minutes, nearly all of it downloading.

**One trap worth carrying forward:** Gradle copies Capacitor's own `native-bridge.js` into
`app/build/intermediates/`, and `eslint .` walked into it and failed the build on a rule its inline
disable comments name. `android` joins `dist` in `globalIgnores`.

## 2. iOS push via Capacitor **[web]** — *the §7A question flagged "the single most important"*

*The web-push alternative is the subject of check B4 and is answered in §11 once observed.*

Native APNs through Capacitor needs, in order:

| Requirement | Blocked on |
|---|---|
| **Paid Apple Developer Program membership** ($99/yr) | **#30** — whether AXA holds one |
| App ID registered with the **Push Notifications** capability | #30 |
| An **APNs key** (`.p8`) from the developer portal, uploaded to FCM if routing through Firebase | #30 |
| **Push Notifications** + **Background Modes → Remote notifications** capabilities in Xcode | — |
| A **physical iPhone** — APNs cannot be tested on the Simulator | covered (developer owns one) |

Two consequences for the plan. First, **none of this can start until #30 is answered**, and provisioning
an Apple Developer account is a multi-week process when the organisation does not already have one —
HANDOFF §12 already says to start it *now*. Second, routing iOS push through **Firebase** (the common
Capacitor path) puts a Google dependency in front of AXA notification delivery, which is a question
for AXA's InfoSec (#21) rather than a developer default; APNs can be addressed directly instead, at
the cost of writing a second sender adapter beside `WebPushSender`.

Reliability when backgrounded or killed is APNs' own, and is not in question — this is the mechanism
native apps use. **What is in question is whether it is needed at all**, and that is §11.

## 3. Android push, and the OEM problem **[web]**

FCM setup through Capacitor is routine: a Firebase project, `google-services.json` into
`android/app/`, the Firebase Gradle plugin, `@capacitor/push-notifications`.

**The real finding is not FCM, it is the handsets.** Aggressive OEM battery management on Samsung,
Xiaomi, Vivo and Oppo kills the background process holding FCM's idle socket, and delivery degrades
sharply when an app has not been opened recently. Android 16's more accurate Doze detection does not
change this — the brand-specific settings dominate the Android version. The standard mitigations are
an in-app prompt via `ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS` (permitted by Play policy) and
per-device guidance of the kind dontkillmyapp.com catalogues.

**And one hard fact that belongs in front of AXA, not in a footnote: Huawei devices launched from 2020
onward ship without Google Play Services, so FCM does not work on them at all.** Delivery requires
Huawei Mobile Services Push Kit — a *second* push integration, a second developer account, and a
second store. Huawei has meaningful share in MENA. **This is a direct dependency on #28 (device mix)
and it is the first thing that could make the BRD's primary requirement — *"a popup message will show
on the expert mobile"* — undeliverable for part of the expert network** through no fault of the
design. It needs to be an explicit question to AXA rather than a discovery in week 6.

## 4. Camera: forcing capture, blocking gallery — *pending A1 / B1*

## 5. Voice recording — *pending A3 / B2*

## 6. Geolocation for Arrived — *pending A2 / B3*

## 7. Building iOS without a Mac **[web]**

**Codemagic's free tier still stands as HANDOFF §4 recorded it (re-checked 2026-08-23):** 500 build
minutes per month on macOS M2, reset on the 1st. The constraints that matter:

- **Personal accounts only.** Free minutes are not available on a Team — so, exactly as §4 says, the
  **CI account stays in the developer's name while the store accounts are AXA's**. That split needs to
  be written into the handover, or it becomes an orphaned dependency the day the developer leaves.
- **One parallel build**, no added concurrency.
- At a fifteen-minute iOS build that is roughly **33 builds a month** — ample for this project.
- Overflow is billed per minute.

Codemagic builds native iOS, Ionic and Capacitor projects, not only Flutter. **Still unverified and
still on the critical path: that *this* Capacitor project actually builds there**, which cannot be
tested until #30 gives an Apple Developer account to sign against. §7 item 7 is therefore *de-risked,
not eliminated* — unchanged from §4's own wording.

## 8. Distribution: store vs MDM **[web]** — *decision belongs to AXA (#29)*

| | Public App Store / Play | Enterprise / MDM |
|---|---|---|
| Review cycles | Yes — days per release, and rejection risk for an app the public cannot use | None |
| Who can install | Anyone | Only enrolled devices |
| Fit for a closed network of AXA experts and garages | Poor — a public listing for a private tool | Good |
| Needs from AXA | Apple Developer Program, Google Play account | An MDM tenant, device enrolment, Apple Business Manager |
| Effect on the schedule | Review latency lands in weeks 7–8, the tightest part of §11 | Removes review from the critical path |

**Recommendation: MDM if AXA already runs one, public stores only if they do not.** The BRD's own
framing — an invitation link sent to a known mobile number — describes a closed network, and §11 has
no room for a store rejection in week 8. This is #29 and the answer changes the week-6 packaging step,
not the architecture.

## 9. Client-side image quality check — *already built; pending A6 for the device measurement*

This question was answered ahead of the research: design.md §7.2's gate shipped in **slice 2.5** —
resolution floor, greyscale → Laplacian → variance against `Clarity.BlurVarianceThreshold`, measured
at a fixed `Clarity.BlurAnalysisMaxEdge` so the threshold means the same thing on a 12 MP and a 48 MP
handset, then a confirm screen. **Explicitly not ML**, per §7A. What the spike adds is the only thing
the browser could not tell us: what it costs in seconds on real glass, on a real photograph. *A6.*

## 10. Is Capacitor still the right pick in 2026? — *pending; written with the go/no-go*

---

## 11. Go / no-go — *pending the hand-tests*

*Not written yet, deliberately. The one question it turns on is B4: whether an installed iOS PWA
receives a real web push, and how hard the install is for somebody who is not a developer. If it does,
the case for a Capacitor **iOS** build weakens sharply, because what the BRD asks for is a visible
popup on assignment — not silent push or background execution, which is what iOS web push genuinely
cannot do (no silent push, no background wake, no Background Sync / Periodic Sync / Background Fetch).
If the install is the obstacle §4 predicts, the Capacitor decision stands on its original reasoning.*

*Note on sources: the widely-quoted "≈33% web push vs 95% native delivery" figures come from vendors
selling native app wrappers and are **not** treated as evidence here. The structural limits above are
Apple's and are not in dispute; the delivery-rate claims are not load-bearing for this decision.*

## 12. Revised estimate for slice 6.3 — *pending*

## Blocked on the client

| # | Question | What it decides here |
|---|---|---|
| **#28** | Device mix across the expert/garage network | Whether **Huawei/HMS** is a second push integration (§3). 80%+ Android with no Huawei shrinks iOS risk to near zero, as §7A predicted |
| **#29** | Store vs MDM | §8's packaging step, and whether store review sits in weeks 7–8 |
| **#30** | Apple Developer account | **Everything in §2 and the Codemagic verification in §7.** Multi-week lead time — start now |

---

## Sources

- [Codemagic — Pricing](https://codemagic.io/pricing/) and [Codemagic Docs — Pricing](https://docs.codemagic.io/billing/pricing/) (re-checked 2026-08-23)
- [Capawesome — The Push Notifications Guide for Capacitor](https://capawesome.io/blog/the-push-notifications-guide-for-capacitor/)
- [Capacitor Documentation — Push Notifications (Firebase)](https://capacitorjs.com/docs/guides/push-notifications-firebase)
- [CleverTap — Why push notifications go undelivered on Android](https://clevertap.com/blog/why-push-notifications-go-undelivered-and-what-to-do-about-it/)
- [Pushwoosh — Android push notifications (2026)](https://www.pushwoosh.com/blog/android-push-notifications/)
- [MagicBell — PWA iOS limitations and Safari support (2026)](https://www.magicbell.com/blog/pwa-ios-limitations-safari-support-complete-guide) — *vendor source; structural limits only*
- [MobiLoud — Do PWAs work on iOS?](https://www.mobiloud.com/blog/progressive-web-apps-ios) — *vendor source; structural limits only*
