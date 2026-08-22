# Pass 3 review — desktop screens

Reviewed 2026-08-22 against the pass-3 brief and the build. Canvas: artifact `54787950-7b8f-4239-9817-8ec9cefa5a8f` ("AXA Motor Claims Desktop Screens"), 15 artboards at 1440 wide.

**Verdict:** complete and consistent. Coverage: DesktopFrame (three role shells), O1 inbox + loading/empty/failed, O2 review (live) + the four decision states, B1–B4 + broker outcome states, A1 list + form, A2 queue + empty, desktop states gallery. Constraints held: no AXA branding, no images, placeholder values only (`PLACEHOLDER-*`, `+999`, `@example.invalid`), accent `#2E6BB8` and IBM Plex reused, pass-1 names intact, 36 px desktop controls.

## What the designer found in the build (all accurate — "Not built" notes on the artboards)

| Screen | Finding | Where it lands |
|---|---|---|
| B1, B2, B4, BrokerStates | No endpoint lists a broker's requests; nothing writes an Option 1 request; no broker media bucket; nothing sends the routed email (recipient columns exist, never written). The only broker route is the one that issues a customer link. | **5.2** — these artboards are its specification: seven states, six columns, a row appears the moment a link is issued. |
| B3 | `/p/{token}` is the friendly web route in front of `/public/{token}` and does not exist yet; "Send by SMS" is off because `PublicLink.DeliveryChannel = copy`. | **5.3** (route), **#24a** (SMS). |
| A2 | No endpoint lists failed rows, none retries one, and the "long pending" column does not exist. | **6.2** — the artboard is its specification. |
| A1 form | The API returns machine codes for the three save refusals (duplicate phone, duplicate NEXT3 ID, invalid number); **the screen prints the raw response body at the admin**. | **Styling slice** — small, real, and the only pass-3 finding that is a defect in shipped behaviour. |
| DesktopFrame | Sign out does not exist; the broker shell does not exist; the admin nav lacks a "Failed pushes" tab (and its count needs an endpoint). | Sign out + shells: **styling slice**. Failed-pushes count: **6.2**. |
| O1 | **The build sorts the inbox oldest-first; the brief said newest-first.** Drawn as built and flagged rather than silently switched — an officer works a queue, so the longest-waiting declaration comes first. | Keep oldest-first. Correct the O1 wording in design.md §5.2 / the playbook 4.2 card when next touched. |

## Decisions

| # | Finding | Recommendation |
|---|---|---|
| 1 | **O1 sort order** — oldest-first (built) vs newest-first (brief). | **Oldest-first.** It is a queue. One `OrderBy` either way; record it. |
| 2 | **A2 shows amber "still trying" rows** (long-pending, with "Retry now") beside red failed rows. The 2.2 notes said A2 lists `failed` only, since the lease returns a dead worker's row to the queue by itself. | **Include pending rows whose next attempt is more than one poll away, read-only except "Retry now" = set `next_retry_at = now` on a `pending` row (never on `processing`).** It answers "where is that photograph" without waiting a day and a half. Scope it in 6.2. |
| 3 | **A2 "Retry all"** — not in the §5.4 spec. | Accept; it is one statement over the same rows. 6.2. |
| 4 | **B3 makes the customer mobile optional** and never shows it on the public page. The build's `broker_request.customer_mobile` exists; whether it is required was never decided. | **Optional**, as drawn — it is only there so the broker knows whose link this is; #24a decides whether it is ever used to send. |
| 5 | **O2 approve is refused while NEXT3 is unreachable; reject still works** — matches 4.1 decision 4. The three-step sequence ("Rendering decision… Uploading… Approving…") is named on screen. | Keep. The 4.2 card already specifies the ordering; the labels are now the strings. |
| 6 | **O2 contact column** falls back mobile → email → em dash, because "the phone number is the messaging". | Keep. |
| 7 | **Decided declarations show the reason to the officer, never to the garage** — consistent with §1 and pass 2. | Keep. |

## Things the screens got right that the build must keep

- Every red banner names the next action; amber means "wait, it may fix itself". Our-own-API failures carry the HTTP status ("Could not load the inbox (503)") and are visually distinct from NEXT3 outages.
- A2's empty state is drawn as reassurance with a "last checked" timestamp, so "nothing has failed" is distinguishable from "this page has not loaded".
- B4 shows the routed recipient address rather than "sent successfully" — if the routing table is wrong, that is the screen where somebody notices. B4 is read-only by rule (editing would put the broker's words in the customer's submission); a wrong submission means a new link.
- A1: deactivate is the one action that confirms; no reactivate, no delete; re-send invite offered only while `invited`; phone locked after creation.
- The link in B3 is shown once, with "cannot be shown again" said out loud; only its hash is stored.
- The operation names on A2 are shown raw in mono — an admin reading them to a developer needs the real string.

## Implementation notes

- Styling slice: desktop shells for officer/broker/admin with Sign out; the A1 error-code → sentence mapping; admin nav gains "Failed pushes" (count wired in 6.2).
- 5.2 / 5.3 / 6.2 prompts should cite the matching artboards as their screen spec instead of re-describing them.
