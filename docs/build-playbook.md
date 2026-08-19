# build-playbook.md — session-by-session build plan

**What this is:** the ordered list of ~25 build slices for the 8-week plan in `design.md` §11, one slice per Claude Code session, each with its prompt. The spec lives in `design.md`; this file only points at it.
**Status:** written 2026-08-19, before week 1. Weeks 1–2 carry verbatim prompts; weeks 3–8 are outline cards marked *expand when reached*.

---

## How to use it

**The session ritual:**
1. Pick the first unticked slice (they are dependency-ordered — don't skip ahead).
2. Start a Claude Code session in **plan mode** and paste the slice's prompt (prepend the standing preamble below if the session is fresh).
3. Review the plan Claude proposes — check it against the design.md §§ the slice cites — then approve and let it execute.
4. Run the slice's **definition of done**. Green = tick the box, write a one-line learning in *Notes* (what surprised you; the week-upgrade session feeds on these).
5. Review the **test diff** yourself before committing. Claude stops before commit by instruction.

**The drift rule (why prompts look thin):** prompts reference `design.md` §N and never restate its content — no transition tables, no column lists, no config values in this file. When a client answer lands, update `design.md` + the placeholder config; this playbook stays untouched. One source of truth.

**The fidelity rule:** at the start of each week from week 3 on, run one short session: *"Upgrade the week-N cards in build-playbook.md to verbatim prompts, using the Notes from completed slices."* Don't expand weeks early — what week 2 teaches changes how week 6's prompts should read.

**The Friday check:** every Friday, compare ticked slices against the week's row. A week that didn't finish is a **day-for-day slip signal** — per `design.md` §11 there is no descope lever left, so the slip is said to the client immediately, not absorbed silently.

**Slice anatomy:** `☐` status · goal · design.md refs · prompt (or outline) · definition of done (DoD) · test focus · notes.

---

## Standing session preamble (paste at the top of a fresh session's first prompt)

> Work on the slice below from docs/build-playbook.md. Start in plan mode: read the design.md sections it cites **before** proposing anything, propose the file list + edge cases + test list, and wait for approval. Rules: placeholder discipline (design.md Appendix A — no client literals outside the placeholder config); never weaken or delete a test to make it pass without saying so; definition of done includes `dotnet test` green and the architecture tests; stop before committing so I can review the test diff.

---

## Week 1 — Foundation

### ☐ 1.1 Solution scaffold + guardrails
**Goal:** the repo compiles, tests run, and the architecture rules are executable.
**Refs:** CLAUDE.md (source layout), design.md §3 (boundaries), §10 (build conventions).
**Prompt:**
> Slice 1.1 — scaffold the solution per CLAUDE.md's source layout: `AxaMotorClaims.sln`, `src/Api` (.NET 10 minimal API, vertical-slice module folders), `src/Api.Tests` (xUnit), `src/Web` (Vite + React + TypeScript strict). Wire EF Core to LocalDB (`Server=(localdb)\MSSQLLocalDB;Database=AxaMotorClaims;Integrated Security=true`) with a first empty migration. Turn on: `nullable enable`, `TreatWarningsAsErrors`, .editorconfig + analyzers, ESLint as a build error. Add a NetArchTest test project encoding design.md §3's boundaries: (1) no feature module references `RealNext3Client` directly — only the DI registration may; (2) nothing in the public module namespace references `INext3Client` or profile/user types; (3) only the outbox worker namespace calls push operations on `INext3Client`. The boundary tests may be red-green'd with placeholder namespaces now and enforced for real as modules appear.
**DoD:** `dotnet build` zero warnings, `dotnet test` green (including arch tests), `npm run build` succeeds in `src/Web`, week-0 hooks now activate (edit a .cs file → format hook runs).
**Test focus:** the three architecture rules; nothing else yet.
**Notes:** _

### ☐ 1.2 Auth — phone OTP, JWT, roles
**Goal:** all five roles can log in; the §2 policy skeleton exists.
**Refs:** design.md §4 (`app_user`, `otp_challenge`, `invite`), §9 (auth model), §2 (role matrix).
**Prompt:**
> Slice 1.2 — implement authentication per design.md §9: the `app_user`, `otp_challenge`, `invite` tables exactly as §4 specifies, phone-OTP login (hashed codes, TTL and max attempts from the placeholder config), JWT with role claim + refresh token, token invalidation on user deactivation, and the invite flow (S1: link → register → OTP verify → active). Create one ASP.NET authorization policy per role matching the §2 matrix, applied at endpoint-group level. Fake `ISmsSender` logs the codes for dev.
**DoD:** register→login→refresh→deactivate-blocks-login works end-to-end via the API; `dotnet test` green.
**Test focus:** OTP expiry, max-attempt lockout, replay/consumed-code rejection, deactivated-user token invalidation, role policy denies cross-role access.
**Notes:** _

### ☐ 1.3 Admin CRUD + audit log
**Goal:** the four profile CRUDs and the append-only audit trail.
**Refs:** design.md §5.4 (A1), §4 (profile tables, `audit_log`), §9 (audit events).
**Prompt:**
> Slice 1.3 — implement Admin A1 per design.md §5.4: CRUD for the four profile types with the §4 field sets, create-issues-invite (via slice 1.2's flow), deactivate semantics as §5.4 specifies. Add the `audit_log` table per §4 and a small append-only audit writer; wire the §9 audit events that exist so far (login success/failure, admin CRUD). Admin screens are browser-only React pages — functional, no styling effort.
**DoD:** create→invite→deactivate round-trip for each profile type; audit rows written; `dotnet test` green.
**Test focus:** deactivation leaves in-flight data untouched; audit rows are append-only (no update/delete path); NEXT3-ID uniqueness on expert/garage profiles.
**Notes:** _

### ☐ 1.4 The five ports + fakes
**Goal:** every external dependency behind its interface with a working, failure-injectable fake.
**Refs:** design.md §6.2 (interface + fake contract), §3 (senders), §8 (notification log).
**Prompt:**
> Slice 1.4 — create the five ports per design.md §6.2 and §3: `INext3Client` (§6.2 signature verbatim), `IAssignmentSource`, `IEmailSender`, `IPushSender`, `ISmsSender`. Implement fakes for all five: seeded claim data for the NEXT3 fake, config-driven failure injection (`Fake:FailureRate`, `Fake:LatencyMs`), senders logging to the `notification` table (§4). Config selection per §6.2 (`Next3:Mode`, `Next3:AssignmentSource`). No real implementations yet. Register in DI so the arch tests from 1.1 now bind against real namespaces.
**DoD:** app boots fully on fakes; failure injection demonstrably causes retries downstream (assert via a throwaway harness test); arch tests green.
**Test focus:** fake NEXT3 honors `clientRef` dedupe (so outbox tests in 2.2 have a realistic target); notification rows logged per send.
**Notes:** _

### ☐ 1.5 Option 2 schema + public-surface skeleton
**Goal:** the public surface exists on day one — cheap now, expensive to retrofit.
**Refs:** design.md §4 (`broker_request`, `public_link_token`), §9.1 (token + abuse controls), §3 (public-surface boundary).
**Prompt:**
> Slice 1.5 — create the `broker_request` and `public_link_token` tables per design.md §4 and the `/public/*` endpoint group skeleton per §9.1: token generation (raw shown once, hash stored), the uniform-404 rule for invalid/expired/locked tokens, per-IP and per-token rate limiting middleware, and upload size/count caps from the placeholder config. No UI yet — endpoints + tests only. The §3 boundary holds: this module touches only its own tables and blob prefix; the 1.1 arch test for the public namespace must now bind for real.
**DoD:** token issue→open→lock lifecycle works via API tests; invalid/expired/locked are byte-identical 404s; rate limiter demonstrably throttles; arch test green.
**Test focus:** §9.1 token lifecycle table (reuse-until-locked, expiry, lock-on-submit), enumeration resistance, rate-limit thresholds from config.
**Notes:** _

---

## Week 2 — Expert core

### ☐ 2.1 Claim cache + assignments
**Goal:** an assignment arrives (from the fake) and an expert sees their claim.
**Refs:** design.md §4 (`claim`, `expert_assignment`), §5.1 (flow + writes), §6.2 (`IAssignmentSource` handler).
**Prompt:**
> Slice 2.1 — implement the assignment path per design.md §5.1 and §6.2: the `claim` cache and `expert_assignment` tables per §4, the single idempotent `AssignmentReceived` handler (dedupe per §6.2), claim-cache refresh rule per §4's `claim` row, and the fake assignment source injecting synthetic assignments on demand. API: expert claim list + claim detail. Push notification via the fake `IPushSender`.
**DoD:** injecting a fake assignment produces the row, the cache fetch, the notification log entry, and it appears in that expert's list only; `dotnet test` green.
**Test focus:** duplicate/replayed assignment is a no-op; stale-cache banner path when the fake NEXT3 is "down"; assignment invisible to other experts.
**Notes:** _

### ☐ 2.2 The outbox
**Goal:** the load-bearing piece, test-first.
**Refs:** design.md §6.3 (everything), §4 (`next3_outbox` — contractual schema).
**Prompt:**
> Slice 2.2 — implement the transactional outbox per design.md §6.3, test-first: the `next3_outbox` table exactly per §4, the one-transaction write helper every producer will use, the `READPAST` dequeue as the sanctioned stored procedure, the worker loop with §6.3's backoff schedule and `failed` terminal state, and `clientRef` idempotency against the fake. Write the failing tests from §6.3's rules first, then implement.
**DoD:** with `Fake:FailureRate` raised, rows visibly retry on schedule and land `sent` or `failed`; atomicity test proves document row + outbox row commit or roll back together; `dotnet test` green.
**Test focus:** atomicity; retry-after-timeout does not duplicate (fake's `clientRef` check); backoff timings; two concurrent workers never double-process (READPAST test).
**Notes:** _

### ☐ 2.3 Media pipeline — server side
**Goal:** a file becomes a `document` row + blob + outbox push, with the lifecycle rules enforced structurally.
**Refs:** design.md §7 (all), §4 (`document`).
**Prompt:**
> Slice 2.3 — implement the server media pipeline per design.md §7: the `document` table per §4, a **streamed** multipart upload endpoint (no buffering into memory — §7.3's failure order: blob PUT first, then the one-transaction rows via 2.2's helper), bucket rules per §7.1 with the `origin` flag, server-side re-validation per §7.2 item 5, and the cleanup job whose delete query structurally joins on outbox `sent` per §7.3. Use Azurite (Docker) or the Blob emulator for local dev — document the choice in CLAUDE.md's commands section.
**DoD:** upload→document row→outbox row→fake push→`sent`→cleanup-eligible, verified by test; a `failed` push's blob survives cleanup; `dotnet test` green.
**Test focus:** §7.3 lifecycle rules (especially never-delete-before-sent), capture-only bucket rejection, oversize/wrong-type rejection.
**Notes:** _

### ☐ 2.4 Expert screens E1/E2 + Arrived
**Goal:** the expert's daily flow in the browser.
**Refs:** design.md §5.1 (E1/E2 rows), §2 (screen gating).
**Prompt:**
> Slice 2.4 — build E1 (claim list, newest-first with media counts) and E2 (claim detail with the Arrived button) per design.md §5.1: Arrived writes per its table row (timestamps + coordinates + outbox `update_arrival`), disabled after first press, blocking explanation on geolocation denial, and Arrived does NOT gate capture (§5.1's recorded interpretation). TanStack Query for server state; logic in hooks, components render.
**DoD:** full flow works in the browser against the fake; Arrived visible in outbox as `sent`; `npm run build` + tests green.
**Test focus:** Arrived idempotence (double-press), geolocation-denied path, list scoped to the logged-in expert.
**Notes:** _

### ☐ 2.5 Capture + clarity gate (the shared hook)
**Goal:** the capture/clarity component that Garage and the public page will reuse — the reuse that pays for Option 2.
**Refs:** design.md §7.1 (bucket matrix), §7.2 (gate), §5.1 (E3/E4).
**Prompt:**
> Slice 2.5 — build the capture component + clarity gate per design.md §7.2 as a **reusable hook + component pair** (it must later serve Garage and the Option 2 public page unchanged): camera capture vs file pick per the §7.1 bucket matrix, resolution floor + Laplacian blur-variance from the placeholder config, the E4 confirm screen, retry loop, then upload via 2.3's endpoint. Buckets configurable per §7.1. Browser `capture` attribute is the hint; native enforcement is Capacitor's job later (§7.1 provisional note).
**DoD:** expert can fill all four buckets end-to-end; blurry/small images are rejected client-side and (bypassing the client) server-side; tests green.
**Test focus:** Laplacian threshold behavior at the config boundary, capture-only bucket refuses file pick, hook reusability (no expert-specific coupling).
**Notes:** _

---

## Week 3 — Expert rest + real NEXT3 · *expand when reached*

### ☐ 3.1 Voice note + damage diagram — §5.1, §7.2(4), §1 simplifications. DoD: both artifacts flow through the 2.3 pipeline into *Expert documents* with placeholder doc types.
### ☐ 3.2 Search + expert report — §5.1 (E1/E5), §6.1 claim search. DoD: visa/plate search against fake; report upload (upload-allowed bucket).
### ☐ 3.3 RealNext3Client + swap — §6.1/§6.2, the OpenAPI proposal. **Gate: #1 sandbox exists. If late: stay on fake, log it in the weekly status, do NOT silently absorb.** DoD: config flip switches implementations; contract tests run against both fake and sandbox.
### ☐ 3.4 Web push + notification log — §8. DoD: assignment and (later) decision pushes arrive in the browser; every send logged.

## Week 4 — Declaration + DEMO · *expand when reached*

### ☐ 4.1 Declaration state machine — §5.2. Transitions as entity methods; illegal transitions unrepresentable; one test per transition-table row incl. the ordered Approve side effects; no resubmit edge (§1 interpretation).
### ☐ 4.2 Garage G1–G3 + Officer O1–O2 — §5.2 rows, §2 gating. Approval-comments PNG render; notifications both ways; approval unlocks detail per §5.2.
### ☐ 4.3 Demo prep — walk §5.1 + §5.2 end-to-end on the fake, fix list, write the demo script.

**Week-4 milestone checklist (client demo → 30% payment):**
- [ ] Fake assignment → expert popup → Arrived → capture → clarity → outbox `sent`, live
- [ ] Garage declaration → officer approve → garage unlock + PNG in fake Survey folder, live
- [ ] Failure-injection demo: kill fake NEXT3 mid-flow, show the queue drain on recovery (this is the outbox's sales pitch)
- [ ] All placeholders visibly fake (nothing that looks like invented client data on screen)
- [ ] Demo script written; environment reset procedure tested

## Week 5 · *expand when reached*

### ☐ 5.1 Repair flow G4 — §5.2 (repairs transitions, bucket split).
### ☐ 5.2 Broker Option 1 — §5.3 (B1/B2 rows): routing via placeholder config, `origin` provenance flag, `Broker.AllowUpload` kill-switch.
### ☐ 5.3 Option 2 public form — §5.3 (P1 fields + doc upload) on the 1.5 skeleton; customer-entered premium per §1.

## Week 6 — Option 2 capture + device checkpoint · *expand when reached*

### ☐ 6.1 Option 2 capture + broker review — §5.3 (P1 capture rows, B3/B4): 5 slots + side selector **reusing the 2.5 hook**, ready-to-send, Send Email.
### ☐ 6.2 Failed-push admin screen A2 — §5.4.
### ☐ 6.3 Capacitor wrapper — §7.1 native enforcement. **Gate: `research-capacitor.md` written (§7A). Android via SDK CLI tools (no Android Studio unless it fights back).**

**Week-6 milestone checklist (real-device checkpoint — Flutter reconsidered if push or camera fail):**
- [ ] Samsung via USB: push arrives app-killed and backgrounded; capture-only bucket cannot reach gallery; Arrived geolocation accurate
- [ ] iPhone (Codemagic build → TestFlight/ad-hoc): same three checks
- [ ] Clarity gate performance acceptable on-device
- [ ] Battery-saver test on the Samsung (MENA OEM push-killing — §7A item 3)
- [ ] Verdict recorded in research-capacitor.md: go / adjust / Flutter conversation

## Week 7 · *expand when reached*

### ☐ 7.1 Public-surface security pass — §9.1 as tests: token lifecycle, rate limits under load, caps, enumeration resistance. Run `/security-review` on the diff.
### ☐ 7.2 Hardening + spillover buffer — error states, size limits, audit completeness (§9 event list); Option 2 spillover lands here by design.
### ☐ 7.3 UAT prep — seed data, test environment live (§10), runbook draft, `/code-review ultra` on the whole tree.

## Week 8 · *expand when reached*

### ☐ 8.1 UAT round — single round, capped fix window (§11 trade-off; scope letter defines acceptance).
### ☐ 8.2 Deploy + handover — prod deploy per §10, handover docs, account transfer, training.

**Week-8 milestone checklist (handover → 30% payment):**
- [ ] Source in AXA's repo; deployment runbook delivered; accounts transferred (AXA name, AXA card)
- [ ] Fake NEXT3 still config-selectable (support/debugging tool per §6.2)
- [ ] Training session held; hypercare window dates in writing
- [ ] "Handover complete" per scope-decisions.md guardrails — deployed and demoed, not AXA's internal rollout

---

## Maintenance rules

- **Client answer arrives** → update `design.md` + placeholder config; playbook untouched (prompts only point).
- **Slice done** → tick + one-line Note; Notes feed the next week-upgrade session.
- **Week starts (3+)** → upgrade that week's cards to verbatim prompts in one short session.
- **Design change** (scope, interpretation) → design.md and scope-decisions.md first; then check whether any un-started slice's refs moved.
