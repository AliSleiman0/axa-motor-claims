# rollout-notes.md — putting the app on people's phones

Written 2026-08-27, slice 7.2, from what the device passes actually measured
(`docs/device-spike-2026-08-22.md`, `docs/oem-push-guidance.md`, `docs/research-capacitor.md` §11).
This is the training-and-support side of the platform split, not a design document: it is what
somebody rolling the app out to experts, garages, officers and brokers needs to say out loud.

**The platform split, in one line.** Android users install the **app** (Capacitor, from the store or
an MDM push — #29/#30). iOS users install the **web app** from Safari. Everybody else — claim
officers, brokers, admins — uses a browser and installs nothing.

---

## iOS: Add to Home Screen

Safari on iOS will not show a claim notification to a page open in a tab. Web push works **only**
from a home-screen install, so this step is not optional for an iPhone user who needs popups; it was
proven on a real handset in the 6.3a spike.

1. Open the app's address in **Safari** — not Chrome, not an in-app browser. iOS gives every other
   browser Safari's engine but not its install path, and a link opened from Mail or WhatsApp lands
   in an in-app browser that has no Share menu item for this.
2. Tap **Share** (the square with the arrow), then **Add to Home Screen**, then **Add**.
3. Open the app **from the new icon** from then on. The tab and the icon are different installs.
4. Sign in, then turn on notifications when the app asks. Grant it — iOS asks once, and a refusal
   has to be undone in Settings rather than in the app.

### The cost nobody expects: a fresh sign-in

**An installed iOS PWA has its own storage.** It does not inherit the session from the Safari tab it
was installed from, so the first thing it does is ask for a phone number and an OTP — even for
somebody who signed in thirty seconds earlier to check the app worked.

That is one extra SMS per person, and it is worth saying *before* they start rather than explaining
afterwards. It is a real cost of the platform (recorded in `scope-decisions.md` at the device
checkpoint), not a bug, and there is nothing to fix.

The practical order for a rollout session is therefore: **install first, sign in once, inside the
installed app.** Anyone who signs in to the tab and then installs will sign in twice.

---

## Android: the app

Android users get the Capacitor app, and the reason is worth knowing because it changes what they
can do: **the Android WebView ignores `capture="environment"` and opens the photo gallery.** In the
browser, a garage or an expert could attach a screenshot or an old photograph to a bucket the BRD
requires to be taken at the scene. The installed app calls the camera directly and offers no gallery
route at all (measured in 6.3, on the handset).

So: **on Android, capture-only means the app.** A user who has been given a browser link and is
told to photograph a car is being given the wrong thing.

Sign-in is the same phone-and-OTP flow, once per install.

---

## Notifications that stop arriving

The single most common support call for this class of app, and it is almost never the app.

- **Battery optimisation.** Most Android manufacturers kill background delivery for apps they decide
  are idle, and the aggressive ones are common in this region. The app has to be excluded from
  battery optimisation by hand, per handset. **The exact per-manufacturer steps, and the order to
  triage a "no notifications" call in, are in `docs/oem-push-guidance.md`** — start there rather
  than reproducing them here, because they change with OS versions and that file is the one that
  gets updated.
- **Huawei handsets have no Google Play Services at all**, so FCM cannot deliver to them. That is a
  device-selection question, not a setting, and it needs to be known before somebody is issued one.
  Again: `oem-push-guidance.md`.
- **A handset that changed hands stops notifying its previous holder, by design.** Registering the
  app on a phone takes it over completely: an FCM token identifies the *install*, not the person, so
  the app assumes the person who signed in last is the person holding it. If two people share a
  pooled phone, the one who wants notifications signs in again — that is all it takes, and it is
  immediate.
- **A deactivated account stops receiving notifications immediately** (slice 7.2), on every device it
  had registered. There is nothing to clean up on the handset.
- **On iOS, notifications only work from the home-screen install.** If somebody says they get none,
  the first question is whether they are in the installed app or in a Safari tab.

---

## What to check before a rollout session

- The address people will type, over **HTTPS**. iOS refuses camera, location and notifications on
  anything else, and gives no useful error when it does.
- One handset of each kind, taken through the whole path: install, sign in, capture a photograph,
  receive a notification. The device passes have found something every time.
- Whether the group is Android, iPhone, or both. The two paths above are different sessions, and
  running them together produces a room where half the instructions are wrong.
- That somebody in the room can send an OTP-bearing SMS and see it arrive; every path starts there.
