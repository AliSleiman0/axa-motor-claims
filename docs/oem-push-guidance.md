# OEM push guidance — why Android notifications stop, and what to tell people

**Written 2026-08-26, slice 6.3.** For the rollout and training notes, and for whoever answers the
first "the app stopped telling me about claims" call.

Audience: AXA's rollout/support side, and the developer during the week-6 device pass. Everything
here is about **Android**; iOS ships as the installed PWA and is not affected (see
`research-capacitor.md` §11 for why the platforms are split).

---

## 1. The problem, stated plainly

The BRD's primary requirement is *"a popup message will show on the expert mobile"*. On Android that
popup is an FCM notification, and FCM delivers it over a socket held open by a background process.

**Several major manufacturers kill that process on their own schedule**, more aggressively than
stock Android does, and more aggressively still for an app that has not been opened recently. The
app is not crashing and nothing is misconfigured: the operating system has decided the app is idle
and stopped it. Delivery then degrades from "instant" to "whenever the phone next wakes it", which
for an expert waiting on a dispatch is the same as not working.

This is not a defect this project can fix in code. `research-capacitor.md` §3 records it as the
**single biggest threat to the BRD's primary trigger in this region**, and it is a rollout problem
with a rollout answer: exclude the app from battery optimisation on each handset, once, at
enrolment.

**The app says so itself.** Once notifications are enabled, the panel shows: *"If claims stop
arriving, check that this app is excluded from battery optimisation — some phones stop background
apps after a day or two, and notifications stop with them."* That is copy rather than an in-app
prompt, deliberately: the `ACTION_IGNORE_BATTERY_OPTIMIZATION_SETTINGS` intent needs a community
settings plugin, and taking a dependency for one prompt means a licence review the slice did not
budget (recorded in `scope-decisions.md`).

---

## 2. Huawei is a different problem, and it is worse

**Huawei handsets launched from 2020 onward ship without Google Play Services, so FCM does not work
on them at all.** Not degraded — absent. There is no setting that fixes it.

What the app does on such a device: `PushNotifications.register()` raises `registrationError`, the
enable button reports a failure, and no `device_token` row is ever created. The expert can still use
the app; they will simply never receive a popup and must open *My claims* to see new work.

Delivering to those handsets needs **Huawei Mobile Services Push Kit** — a second push integration, a
second developer account, and a second store listing. That is **not budgeted anywhere** and depends
on **open question #28** (the device mix across the expert and garage network). It is the first thing
that could make the BRD's primary requirement undeliverable for part of the network through no fault
of the design, and it belongs in front of AXA rather than in a footnote.

---

## 3. Per-manufacturer steps

Menu names drift between OS versions and regional builds; these are the paths as of the versions
current when this was written. The pattern is always the same: find the app, turn battery
restrictions **off**, and allow it to run in the background.

### Samsung (One UI) — the handset the device pass runs on
1. **Settings → Apps → AXA Motor Claims → Battery → Unrestricted.**
2. **Settings → Battery → Background usage limits** — confirm the app is **not** in *Sleeping apps*
   or *Deep sleeping apps*. Remove it if it is; a deep-sleeping app receives nothing.
3. **Settings → Battery → Background usage limits → Put unused apps to sleep — off**, or the app
   returns to the sleeping list after a few days of not being opened. *This is the setting that
   makes the problem come back weeks after enrolment.*

### Xiaomi (MIUI / HyperOS) — the strictest of the common ones
1. **Settings → Apps → Manage apps → AXA Motor Claims → Battery saver → No restrictions.**
2. Same screen: **Autostart → on.** Without autostart the app cannot be woken by a push after a
   reboot at all.
3. **Lock the app in Recents** (open the task switcher, pull the app's card down or tap the padlock)
   — MIUI treats a locked task as exempt from cleanup.
4. **Settings → Battery → Battery saver — off** for this app.

### Huawei / Honor
Do the steps below **only on a device that has Google Play Services** (pre-2020, or a build with
GMS). On anything newer, see §2 — no setting helps.
1. **Settings → Apps → AXA Motor Claims → Battery → App launch → Manage manually**, then enable all
   three of *Auto-launch*, *Secondary launch* and *Run in background*.
2. **Settings → Battery → More battery settings → Stay connected when asleep — on.**

### Oppo / Realme (ColorOS) and Vivo (Funtouch/OriginOS)
1. **Settings → Battery → App battery management → AXA Motor Claims → Allow background activity**
   (Oppo) or **Settings → Battery → High background power consumption → allow** (Vivo).
2. Both: **Settings → Apps → AXA Motor Claims → Auto-launch / Startup manager → on.**
3. Vivo additionally: **Settings → More settings → Applications → Autostart → on.**

### Stock Android (Pixel, most others)
1. **Settings → Apps → AXA Motor Claims → App battery usage → Unrestricted.**
2. Nothing else is normally needed; stock Doze is not the problem this document is about.

### Any device, Android 13+
**Settings → Apps → AXA Motor Claims → Notifications** must be allowed. This is the
`POST_NOTIFICATIONS` runtime permission, asked once on first enable — if it was dismissed, the app
cannot re-ask and the person has to grant it here.

A useful external reference kept up to date per manufacturer: <https://dontkillmyapp.com>.

---

## 4. Support triage — "I stopped getting claim notifications"

In order, because each step rules out the one below it:

1. **Did they ever enable them?** The panel says *"Notifications are on"* only after the button was
   pressed on that handset. In the shell the app cannot tell whether the server holds a registration,
   so it always offers the button — pressing it again is harmless and re-registers.
2. **Was the handset handed over?** Registering a phone **revokes it for whoever held it before**
   (design.md §4, `device_token`). If two experts share a device, only the one who signed in most
   recently receives popups. The other takes it back by signing in and pressing enable again.
3. **Check the `notification` table** for that user — one row per device per send, with the error.
   A row saying the user has no registered device means step 1 or 2. A `failed` row with an FCM
   status is a delivery problem; a `sent` row means AXA's side did its job and the handset is where
   the notification stopped, which is what this document is about.
4. **Is it a Huawei?** See §2. Stop here — no setting will fix it.
5. **Battery optimisation**, per §3. This is the common answer when the app worked for a while and
   then stopped, especially after a few days of not being opened.
6. **Reinstall as the last resort.** FCM issues a new token, the app registers it on next launch, and
   the old row is left revoked by its next failed send. Cheap, and it clears a stale token that
   nothing else prunes (the `device_token` retention gap is a 7.2 ticket).

---

## 5. What this means for the rollout

- **Enrolment must include the battery step**, per handset, at the same time as installing the app
  and completing the OTP sign-in. Left to the expert to do later, it will not happen, and the failure
  appears weeks afterwards as "the app is unreliable".
- **Ask AXA for the device mix (#28) before rollout planning**, not after. If Huawei is a meaningful
  share of the network, that is a scope conversation and a budget conversation, not a support script.
- **Ship this document with the training notes**, not only with the code — the person who needs §3 is
  whoever is standing next to the expert with the phone in their hand.
