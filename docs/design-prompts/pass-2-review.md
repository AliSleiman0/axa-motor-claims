# Pass 2 review — phone screens

Reviewed 2026-08-22 against the pass-2 brief and the build. Canvas: artifact `3a992847-adf6-428b-b195-e454d1555caf` ("AXA Motor Claims Phone Screens"), 32 artboards at 390 wide.

**Verdict:** complete and consistent. Every brief item is covered (S1 ×4, S2 ×3, E1 ×4, E2 ×6, push placement, G1 ×2, G2, G3 ×5 on separate artboards, G4, P1 ×4, PhoneNav). Constraints held: no AXA branding, no images, every value a `PLACEHOLDER-*` / `PLC-TEST-*` / `+999` fake, accent `#2E6BB8` and IBM Plex reused, pass-1 component names intact. Several artboards carry "contract notes" describing what the API does today versus what the screen assumes — those are the decisions below.

## Decisions (resolve before the styling slice; none block pass 3)

| # | Finding | Options | Recommendation |
|---|---|---|---|
| 1 | **PhoneNav's "Notifications" tab has no screen behind it.** Drawn so a missed popup is recoverable; the designer flags it as an open question. | (a) Build a notifications list — net-new scope. (b) Drop the tab; with one destination left, drop the bottom nav entirely (E1/G1 are the recovery; the header carries Sign out). | **(b)**. Record in scope-decisions; revisit only if UAT asks. |
| 2 | **Invite SMS carries the raw token, no link** — S1's "paste the invitation" path exists only because of that. | Put a URL in the SMS + add the web route (one line + a route). | **Accept.** Fold into the styling slice. |
| 3 | **P1 names the broker** ("PLACEHOLDER Broker One asked you to complete this") — but the public API deliberately returns nothing identifying before submission, and a 1.5 test pins that. The designer's case: an anonymous page asking the public for identity documents looks like phishing. | Snapshot the broker's display name onto `broker_request` at link creation (no cross-module reference, architecture rule 2 untouched) and return it from `GET /public/{token}`. | **Accept**, via the snapshot. design.md §9.1 "what the page exposes" gains the broker display name (it was the original intent). |
| 4 | **G1's media count includes the officer's approval image** — garage uploaded 2, sees 3. | Exclude `approval_image` from garage-facing counts. | **Exclude.** |
| 5 | **G3 Repairs proposes "Submit disabled until an invoice exists."** The BRD says "documents such like discharge, invoice" — invoice-required is an invented rule. | At least one repair document, any bucket. | **At least one.** Decide in 5.1; the three-panel split itself is fine. |
| 6 | **Expert report panel is file-only (no camera).** design.md §7.1 marks report capture "n-a", but the build deliberately refuses nothing captured (3.1: refusing a captured report is an invented restriction). | Show both controls, as the build allows. | **Both.** |
| 7 | **String splits:** document rows say "Queued" where pass 1's `PushIndicator` says "Queued, will send". P1 shows amounts as "0.00" with no currency because none was ever specified. | Pick one indicator string; raise currency as a client question (#14 family). | "Queued, will send" on phone rows too; **add the currency question** to open-questions. |

## Things the screens got right that the build must keep

- Two distinct E1 empty messages (no claims vs no match) and the search field above every branch.
- Two E2 banners, never one: stale (amber) vs not found (red); a third case with nothing cached shows only the unreachable message.
- Not accepted shows status and nothing else — and offers "New declaration" rather than leaving the terminal state to be discovered.
- Every refusal names what to do differently; the uniform 401 / uniform 404 contracts are respected on screen (S2, S1Expired, P1Expired each say "may" rather than naming a cause).
- Capture is not gated on Arrived; the Arrived latch disables before the request leaves.
- The S2 resend countdown is read from the server's answer, not guessed — **check the 429 actually carries the seconds.**

## Implementation notes for the styling slice

- PhoneNav: per decision 1, likely removed; AppHeader gains Sign out (net-new, pass 1).
- Per-bucket accessible names ("Take a photo — insured car") as drawn on E2Capture and G3Draft.
- G3Repairs and P1Capture are **proposals for 5.1 / 5.3–6.1**, not descriptions of the build; their contract notes say so on the artboard.
