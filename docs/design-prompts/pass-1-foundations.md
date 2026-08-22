# Design prompt — Pass 1: Foundations

**How to run:** in Claude Code, in this repo, type `/design` and paste everything below the line as the brief, in one message. Review the canvas, adjust tokens/components there, export PNG/PDF if wanted. Run pass 2 only after pass 1 is settled — every later screen is built from these components.

---

Design the complete screen set for the AXA Motor Claims app described in docs/design.md (loaded in context). The BRD supplied no UI/UX; this is the product's first visual design. This run is PASS 1 of 3: foundations only — tokens, components, and a states gallery. Do not draw product screens yet.

PRODUCT & USERS
- Five authenticated roles + one public customer (design.md §2). Field roles are PHONE-FIRST: Expert and Garage use a Capacitor-wrapped app at crash sites and workshops, one-handed, often in sunlight. Claim Officer, Broker and Admin work on DESKTOP browsers. The Option 2 public customer page is phone-only, opened from a link, no login.
- Tone: calm, institutional, trustworthy insurer — not a consumer app. Dense enough for officers, spacious enough for gloves-off roadside use. Large tap targets (min 44 px) on every phone control.

HARD CONSTRAINTS (from the build — do not design around them)
- NEUTRAL PALETTE, NO AXA BRANDING. Use a placeholder wordmark "AXA Motor Claims" in plain type; one neutral accent colour; mark every token that would swap for brand values later.
- Every visible data value must look obviously fake: PLACEHOLDER-VISA-0001, PLC-TEST-01, PLACEHOLDER Insured One, +999 000 000 001. Never invent realistic names, plates or amounts.
- Capture-only photo buckets show a CAMERA action only — no "choose file" control exists for them. Document buckets show both. The expert screen has five buckets, each needing a distinct, readable label and a count (the current build labels them identically — fix that).
- The clarity-confirm step is one shared component: full-bleed photo, a caption like "1600×1200, sharpness 312", Retake / Confirm. Audio variant: filename + player + "Listen before sending". Non-image variant: filename only.
- Outage is a first-class state: a "NEXT3 unreachable — showing last known details" banner, and uploads that still succeed while a queue grows (the app never blocks the field user). Design that banner and a subtle "queued, will send" indicator for documents.
- Push: an "Enable notifications" panel with four states — on / off / blocked by browser / unsupported.
- Dates are formatted local time; missing values are an em dash.

DELIVERABLES
- Artboards at 390×844 (phone) and 1440×900 (desktop) where a component needs both renderings.
- Component names written on the artboards and kept consistent — passes 2 and 3 reuse these names verbatim.
- Light mode primary, dark-mode-safe tokens.

DELIVER NOW — PASS 1: FOUNDATIONS
1. Design tokens: type scale, spacing scale, neutral palette + one accent, radii, elevation, focus ring. Annotate which tokens are "brand-swappable later".
2. Component sheet, each with its variants:
   - Buttons: primary / secondary / destructive; each with a pending variant whose label changes ("Sending…", "Enabling notifications…", "Finishing…"); disabled.
   - Text input, search input, textarea, phone-number input (the login field), with error state.
   - Status chip for the declaration states: Draft, Waiting for AXA, Approved, Not accepted, Repairs in progress, Repair documents submitted.
   - Alert banner (error / blocking) vs status banner (informational) — visually distinct, both with an icon and plain-language text.
   - Key-value detail table (claim fields) and a worklist row/card (visa, plate, insured, received, media count, arrived).
   - Capture panel: camera-only and camera+file variants, with label, count, and the "Loading photo settings…" / "not configured" rungs.
   - Clarity confirm: image / audio / file variants.
   - Voice recorder: idle / recording / finishing.
   - Car damage diagram: 15 tappable panels, marked vs unmarked, plus its "Marked: bonnet, front left wing" caption and Clear / Use this diagram actions.
   - Push panel: on / off / blocked by browser / unsupported.
   - App header with role name and sign-out; phone bottom navigation (field roles: Claims / Declarations, Notifications); desktop top navigation (office roles).
   - Document list item with "queued, will send" / "sent" / "kept" indicator.
3. One "states gallery" artboard: every component's variants side by side, labelled.
