# Pass 1 summary — the design system contract

Extracted 2026-08-22 from the pass-1 canvas (artifact `48978602-331d-46e1-98c8-12cd5bd42bda`, 13 artboards). **Paste this block at the top of the pass-2 and pass-3 briefs when running them in a fresh session**, so the screens reuse these names and values instead of reinventing them. It is also the spec for the styling slice.

---

DESIGN SYSTEM CONTRACT (from pass 1 — reuse verbatim, do not redraw)

Type — IBM Plex Sans (UI) + IBM Plex Mono (references), 16 px base. Reference numbers (visa, plate, phone) are always mono and tabular.
- display 28/1.2/600 · title 22/1.25/600 · section 17/1.3/600 · panel 15/1.35/600
- body 16/1.5/400 · compact 14/1.45/400 · label 13/1.3/500 · caption 12/1.4/400 · mono 14/1.4/500
- Input text is 16 px everywhere (below that iOS zooms the page on focus).

Colour — cool slate neutrals; one accent, used for the single primary action on a screen and nothing decorative.
- Neutral: ink-900 #0F1418 · ink-700 #2B333B · ink-500 #5F6C79 · line-300 #D6DCE1 · line-200 #E4E9ED · canvas #EEF1F4 · surface-100 #F4F6F8 · surface-0 #FFFFFF
- Accent (the ONLY brand-swappable slot): accent-600 #2E6BB8 (default) · accent-700 #245793 hover/pressed · accent-100 #E7EFF9 selected-row / info tint. accent-700 and accent-100 derive from accent-600.
- Semantic (chip tint / text): neutral (Draft) · warn #FBF1DE / #8A6116 (Waiting for AXA) · ok #E8F2EC / #2F6B4F (Approved) · danger #FBEBE9 / #A33A31 (Not accepted) · accent tint (Repairs in progress) · ink (Repair documents sent). Every chip pairs tint + border + text so it survives greyscale and colour-blind readers.
- Dark-safe counterparts exist for every token (bg #0F1418, surface #171E24, line #2C363E, muted #93A0AB, ink #E9EEF2, accent #6AA3E8, ok #7FCBA4, danger #E88A80). Light mode ships; dark is documented as out of scope for the 8-week build.

Shape — radii 4 (inputs/chips) · 6 (buttons/cards) · 10 (panels) · 999 (pills). Shadows: sm `0 1px 2px rgba(15,20,24,.06)`, md `0 4px 12px rgba(15,20,24,.10)`. Focus ring: 5 px accent halo, same offset in both themes.

Control sizes — control-touch 48 px (Expert, Garage, public customer) · control-desktop 36 px (Officer, Broker, Admin) · tap-min 44 px (icon buttons, row actions, nav) · row-min 56 px (worklist and document rows).

Components (names are the contract):
- Button/Primary · Button/Secondary · Button/Destructive (outlined, never filled — Reject sits beside Approve) · Button/Icon (44) · Button/Link. One primary per screen. Pending never spins: the label becomes the gerund and the control disables.
- TextField · PhoneField (E.164, mono, numeric keypad) · OTP code (one field, one-time-code autofill) · SearchField (stays mounted while loading) · TextArea (3 rows min, grows). Labels always visible above; errors are a plain sentence under the field with the HTTP status in parentheses ("Could not request a code (400).").
- StatusChip — six labels verbatim: Draft · Waiting for AXA · Approved · Not accepted · Repairs in progress · Repair documents sent. Two sizes; never a button.
- AlertBanner (role=alert; filled + bordered on intent colour; something did not happen) vs StatusBanner (role=status; quiet neutral strip; where things stand). Both: icon + whole sentence, never a code.
- PushIndicator — grey text, three states: "Queued, will send" · "Sent to AXA" · "Sent to AXA; the local copy has been removed".
- DetailTable (desktop two-column / phone label-above-value; em dash for missing; insured phone is tap-to-dial, nothing else is a link) · WorklistRow (desktop, whole row is the hit area) · WorklistCard (phone) · DocumentRow.
- CapturePanel — camera+file and camera-only variants (a capture-only bucket has NO file control, not a disabled one); count in the heading; rungs "Loading photo settings…" / "Checking the photo…" / "This section is not configured for uploads yet…" kept distinct. Every control carries a qualifier that is its accessible name: "Take a photo — insured car", "Choose a file — insured document". Garage buckets: Survey documents (camera+file), Car photos (camera only).
- ClarityConfirm — image (full-bleed + "1600×1200, sharpness 312") · audio ("Listen before sending:" + player, no caption) · file ("Ready to send: name", never previewed). Gate refusals return to CapturePanel with an alert that says what to do differently.
- VoicePanel — idle ("Record a voice note") · recording ("Recording…" + Stop + level meter) · finishing ("Finishing…"); refusals for microphone denied and browser cannot record.
- DiagramPanel — 15 panels (Front bumper, Front left wing, Bonnet, Front right wing, Front left door, Windscreen, Front right door, Roof, Rear left door, Rear right door, Rear window, Rear left wing, Boot, Rear right wing, Rear bumper); marks kept in tap order; caption is the readout; Clear (disabled until marked) · Use this diagram → ClarityConfirm/image with no sharpness line. Panel names are drawn into the exported PNG; mark fill light enough to keep names readable.
- PushPanel — off ("Enable notifications") · on ("Notifications are on.") · blocked by browser (button stays; explains the padlock) · unsupported (no button; "will still appear in My claims"). Copy is per role: expert = claims assigned, garage = decisions reviewed. Present in expert and garage shells only.
- AppHeader (wordmark "AXA Motor Claims" plain type 17/600 + role name + phone + Sign out) · DesktopNav (office roles; single tab kept for shell consistency) · PhoneNav (two destinations only: Expert = Claims / Notifications, Garage = Declarations / Notifications; 64 px + safe area; back is an in-page "← My claims" link carrying the active search).

Pending labels (shipped strings — reuse exactly, add a new gerund here rather than a spinner): Submit to AXA → Sending… · Confirm → Sending… · Arrived → Sending… · Enable notifications → Enabling notifications… · Stop → Finishing… · Create declaration → Creating… · Start repairs → Starting… · Approve / Reject → Working… (both disable together) · Search → Searching… · Open file → Opening…. Ellipsis is the single character …

Placeholder rule: every value is deliberately fake — PLACEHOLDER-VISA-0001, PLC-TEST-01, PLACEHOLDER-POL-0001, PLACEHOLDER Insured One, PLACEHOLDER Make One, +999 000 000 001. Dates "20 Aug 2026, 09:30".

---

## Product decisions pass 1 introduced (accept or adjust before implementing)

1. Damage-diagram PNG draws panel names into the image and uses a lighter mark than the build's `#cc0000` — changes the artefact NEXT3 receives.
2. Distinct accessible names per capture control — this *is* the week-6 a11y fix, pulled forward.
3. Sign out and the phone bottom nav are net-new (neither exists in the build).
4. OTP as a single field with one-time-code autofill (build ships a plain text input).
5. Terminal chip label is "Repair documents sent" — 5.1 must ship that string.
6. Fonts: the canvas loads IBM Plex from Google Fonts; the app must **self-host** it (Capacitor at a crash site cannot depend on a font CDN) with the system stack as fallback.
