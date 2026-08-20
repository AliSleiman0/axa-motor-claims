# HANDOFF — read this first

**Project:** AXA Middle East — Mobile Application for Motor Claim Management
**Developer:** solo (Ali Sleiman)
**Commitment:** 2 months, $5,000 fixed, developer handles everything
**Status as of 2026-08-20 (later that day — slice 2.4):** slices 2.2 and 2.3 are **committed** (`d099c93`); **slice 2.4 is complete and uncommitted**, awaiting the developer's test-diff review — so the working tree is 2.4 alone. 283 xUnit tests green plus a **new 20-test web suite** (`npm test` in `src/Web` — Vitest + jsdom, stood up by 2.4 because the geolocation-denied path and the double-press guard are browser facts `dotnet test` cannot reach, and 2.5's clarity gate is entirely client-side). 2.4 is the first expert-facing screen work and the first *user action* to put a row in the outbox: E1 lists the expert's claims newest-first with media counts, E2 shows the claim and the **Arrived** button, and pressing it stamps `arrived_at` + coordinates and queues an `update_arrival` push in one transaction. Verified in the browser end to end against the fakes — arrival landed `sent` in `next3_outbox`; with location denied, the screen shows the blocking explanation and **no request leaves at all** (zero outbox rows, zero audit rows for that assignment). No migration: 2.1 created the arrival columns and 2.2 created `EnqueueArrival`.

**The `db-reviewer` pass earned its keep for the third slice running, and this time the worst finding was one the tests had *pinned*.** `ArrivalInfo` carried `DateOnly` + `TimeOnly` — §6.1's own words, written in 1.4 when nothing filled it. The moment 2.4 filled it from a UTC instant, the offset was discarded **at capture**: an expert arriving at 01:30 GST would be pushed to NEXT3 as arriving the previous calendar day, on the single field a claims dispute turns on (§9), in a row that can sit in the queue for 26 hours and that cannot be repaired from its own payload. It is a `DateTimeOffset` now; the split moved to `RealNext3Client` under a new `Next3:ArrivalTimeZone` placeholder, and design.md §12/Appendix A were updated rather than diverged from. The bitter part: the web layer had caught the *same* hazard on the display side that morning (bare timestamps read as local, "a four-hour error in GST that nobody would spot") and nobody connected it to the push side. **A lossy conversion belongs at the edge that knows the target format, never at the producer** — and a test that asserts the code's own arithmetic back to itself can never go red. The same pass also found that the new explicit transaction had to run inside `Database.CreateExecutionStrategy()`, or the day someone enables `EnableRetryOnFailure` (the standard Azure SQL fix, and §10 puts production there) Arrived breaks on *every* press with nothing in the suite to catch it; and that `ApiFixture` was proving both of this project's concurrency guarantees on LocalDB with `READ_COMMITTED_SNAPSHOT` **off**, while Azure SQL has it **on** — it is switched on now, all 283 pass under it, and the arrival guard was re-verified to discriminate under it.

**The 2.4 review point:** *"idempotent" had to be enforced in the `UPDATE`'s `WHERE` clause*, not by an `if`. Four simultaneous presses all pass a read-then-write check, and the consequence is not cosmetic — two `arrived_at` values and two pushes tell NEXT3 the expert arrived twice, at two different times. `WHERE arrived_at IS NULL` settles it; removing the clause was verified to turn the concurrency test from 1 arrival into 4. Because `ExecuteUpdate` runs outside `SaveChanges`, the endpoint opens the codebase's **only explicit transaction** so §4's one-transaction rule holds. A concurrency token on `ArrivedAt` — the 1.5/2.2 idiom — was rejected: EF applies a token to every write of the entity, so a concurrent E2 first-open would fail its `opened_at` save. **A guard that belongs to one transition should not be pinned to the whole row.** The same class of bug appears one layer up in the browser: `mutation.isPending` cannot stop a double tap in the same tick, because state has not landed yet — only a `useRef` latch does, and swapping it back was verified to turn one request into two.

**Status as of 2026-08-20 (earlier):** **Week 1 complete and committed** (1.1 scaffold + arch tests, 1.2 phone-OTP auth, 1.3 admin CRUD ×4 + append-only audit log + browser admin pages, 1.4 the five ports + fakes, 1.5 Option 2 schema + public-surface skeleton — all in git through commit `71023c9`). **Week 2: slice 2.1 (claim cache + expert assignments) is committed (`826e550`); slices 2.2 (the transactional outbox) and 2.3 (the server media pipeline) are complete and uncommitted, awaiting the developer's test-diff review.** 263 tests green; app boots on pure fakes. 2.2 is the load-bearing piece: every NEXT3 write now commits in the same transaction as the domain row that caused it, and a worker drains the queue with a `READPAST` stored procedure, retrying on §6.3's schedule and landing `sent` or `failed`. 2.3 is what finally puts a *file* into that queue — an expert streams a photo to `POST /api/expert/assignments/{id}/documents`, it lands in Blob, and a `document` row plus its outbox row commit together — and it makes §7.3's central rule real: **no blob is deleted before its outbox row is `sent`**, enforced by a structural join rather than by convention. **No client answers received; §8 communications still unsent.**

> ## ⏭️ NEXT SESSION: commit slice 2.4, then slice 2.5 (capture + clarity gate)
> Build proceeds per `docs/build-playbook.md` — week 1 is 1.1–1.5 ☑, week 2 is 2.1–2.4 ☑ (2.2 and 2.3 committed as `d099c93`; 2.4 pending commit); next unticked slice is **2.5**. Slice learnings are in the playbook's Notes lines.
>
> **2.5 inherits from 2.4:** a **web test runner** (`npm test` — Vitest + jsdom + @testing-library; cleanup is wired by hand in `src/testing/setup.ts`, because with `globals: false` the library's own auto-cleanup never registers and every render stacks into the same document). **TanStack Query** is the server-state layer — logic in hooks under `src/Web/src/expert/`, components render, and `src/api/queryClient.ts` holds the defaults. E2 (`ExpertAssignmentPage.tsx`) is where the capture UI goes, and it must stay **ungated on arrival** (§5.1's recorded interpretation, asserted server-side by `ArrivedIsNotAPreconditionForCapture`). The upload endpoint and `MediaBuckets` from 2.3 are what the capture component posts to; note its multipart contract requires the metadata parts **before** the file part. `navigator.geolocation` and, by the same trick, any browser API can be stubbed with `Object.defineProperty` in both jsdom and a real Chrome tab — which is how the denied path was driven without a permission prompt blocking the automation.
>
> **Review 2.3 with these four in mind** (all in the playbook's 2.3 Notes and scope-decisions.md). **A stray `AsNoTracking()` in a composed subquery silently disarmed the retention sweep** — `OutboxSentQuery` projected to a `Guid` and looked free to mark no-tracking, but EF applies the tracking behaviour of the *combined* expression tree, so the outer query came back untracked, `blob_deleted_at` was never written, and `SaveChanges` reported success having done nothing: the bytes were deleted while the row still said they were held. Only the test asserting the *flag* caught it. **Architecture rule 4 had made `OutboxWriter` uncallable** — a called method's return type is part of the calling type's IL, so returning `Next3OutboxMessage` put every producer in violation; it returns `Guid` now, and 2.2 only looked fine because it shipped with no producers. **The `db-reviewer` pass again found more than the tests, and its worst finding was in my fix rather than the design**: the retention sweep deleted up to 500 blobs and committed once, so one undeletable blob discarded the `blob_deleted_at` and audit rows already staged for bytes that were physically gone — and the deterministic batch then stalled retention behind it for ever. It is one document per transaction now. And **`document` has two columns §4 did not list** — `outbox_message_id` (the structural join key §7.3 demands; no FK, because rule 4 keeps `Next3OutboxMessage` out of this module) and `blob_deleted_at` — with design.md §4 updated rather than quietly diverged from, exactly as 2.2 did with `visa_no`.
>
> **2.4 inherits:** the upload endpoint and `MediaBuckets` (§7.1 as data) are what 2.5's capture component posts to; E1 already returns `mediaCount`; `Retention__CleanupEnabled=false` joins `Outbox__WorkerEnabled=false` in `ApiFixture`, and the cleanup sweeps are driven by `CleanupRunner.RunOnce`. Local dev now wants **Azurite** (`Blob:Mode=azure` via launchSettings; `--skipApiVersionCheck` is mandatory — see CLAUDE.md), but `dotnet test` still needs nothing running.
>
> **Review 2.2 with these five in mind** (all in the playbook's 2.2 Notes and scope-decisions.md). **The test diff includes one deletion**: `FailureInjectionHarnessTests.cs`, 1.4's throwaway harness, whose own doc comment scheduled it for removal here. **§4's `claim_id uniqueidentifier` was unimplementable** — no uniqueidentifier claim id exists in the model and this table may hold no FK to the disposable `claim` cache — so it is `visa_no nvarchar(50)` and **design.md §4 was corrected**, not quietly diverged from. **The dequeue procedure gained `@now` and `@lease_seconds`**: without the first, a frozen `FakeTimeProvider` against `SYSUTCDATETIME()` makes every backoff timing untestable; without the second, a worker that dies mid-push strands its row in `processing` for ever, never retried and absent from A2's list. **"No duplicates" does not test READPAST** — two concurrent claims are disjoint either way, the second one just blocks — so the real test holds a transaction open and gives the other connection a short timeout; removing the hint was verified to turn it red. And **`OutboxWorker` was already a registered hosted service**, so the loop is disabled in tests (`Outbox__WorkerEnabled=false`) and `OutboxProcessor.RunOnce` is driven explicitly — the same trap waits for 2.3's cleanup job.
>
> **The `db-reviewer` pass found a real defect the tests had missed, and should now run on every migration.** The lease created a two-owner window — a reclaimed row is held by the new worker *and* the original, and without an arbitrator the slow one wins by arriving last, rewriting a `sent` row to `failed` or giving a `failed` row a `sent_at`. Since §7.3 deletes blobs on `sent` and A2 lists `failed`, both would then act on a status NEXT3 never agreed to. **`attempts` is the concurrency token now** — a reclaim increments it, so it is the claim's generation counter, and §4 needed no new column. It also caught that a row which keeps killing its worker would be reclaimed for ever (the give-up rule runs only when a worker survives), so the dequeue retires those itself via `@max_attempts`. Lesson worth keeping: **a fix for one hole often digs another beside it — audit the fix, not just the original design.**
>
> **What 2.3 inherited from here, and did:** the `document` row is written through `OutboxWriter` in the caller's transaction (architecture **rule 4** forbids any type outside `Api.Outbox` from touching `Next3OutboxMessage`, so there is no other way — which is also how the return-type problem above surfaced), and §7.3's **"no blob is deleted before its outbox row is `sent`"** is now enforced by a structural join. 2.3 also inherited the queue-clearing discipline: the dequeue claims across the whole table and cannot be scoped to one test's rows.
>
> Carried from 1.5: `TokenHashing` in `Api.Infrastructure` backs invites, refresh tokens and public links; architecture rule 2 keeps `Api.Modules.PublicSurface` clear of `Users` and `Next3`, and a `Rule2_IsNotVacuous` test stops that rule passing on an empty namespace. Note rule 2 is scoped to the public module only — `Api.Modules.Expert` may and does reference both. Carried from 2.1: **`Time.Advance` beyond 15 minutes expires the access token** (`ClockSkew` zero), surfacing as a 401 rather than an assertion failure — outbox tests dodge it only because they drive the processor directly instead of over HTTP.
> Still outstanding and getting more urgent as week 1 burns down: **§7A** (Capacitor research → `docs/research-capacitor.md`, validates the provisional Capacitor decision — needed before week 6, ideally sooner) and **§8** (blocking questions, scope letter, OpenAPI proposal — all still unsent; the scope letter must precede real client exposure).
>
> **Schedule decision 2026-08-19 (design.md §11):** Broker Option 2 stays IN scope; the calendar **holds at 8 weeks** — paid for by dropping **damage-diagram polish** and the **second UAT round**, plus pipeline reuse. No descope lever remains; any further slip moves the date day-for-day. `scope-decisions.md` and `estimate-and-plan.md` were reconciled to match the same day. Do not re-litigate Option 2 or the re-cut.

---

## 1. What this project is

AXA Middle East sent a BRD (`docs/source/`). Today their motor-claim photos travel by **email**: experts photograph accident damage at the roadside and email it in; one staff member ("Joanna") manually downloads thousands of emails and re-uploads each photo under the right visa number in **NEXT3** (AXA's claims core system). Expert reports take **2+ weeks**, so the claims team can't make a preliminary assessment or answer the insured / third party in the meantime.

The app replaces that email path: field users capture photos in-app, and the app pushes them into NEXT3 under the correct visa number.

Four profiles: **Expert**, **Garage**, **Claim Officer**, **Broker**.

Source material: `docs/BRD-extracted-text.md` (full text), `docs/diagrams/` (their four flowcharts), `docs/source/` (original .docx).

---

## 2. Stack — DECIDED

Changed from the initial proposal (was Next.js + Postgres). Current stack is the developer's own, and it is **the better choice for this client**: he is fast in it, and AXA is an Azure / Microsoft / almost-certainly-SQL-Server shop that will own and support this software after handover.

| Layer | Choice |
|---|---|
| API | **.NET 10** (LTS), containerized |
| Data | **Azure SQL Database**, **EF Core** + targeted stored procedures |
| Worker | .NET background service or Container Apps Job (outbox processor) |
| Web | **React + TypeScript**, built as a PWA |
| Mobile | **Same React app wrapped in Capacitor** — Flutter deferred, see §4 |
| Hosting | **Azure Container Apps**, `minReplicas: 1` |
| File storage | **Azure Blob** (transit buffer only) |

**Stored procedures: use surgically, not everywhere.** EF Core migrations + LINQ for the app's own domain — 27 open questions means requirements will move in weeks 3–6 and iteration speed matters more than purity. Reserve stored procs for three places that earn them: the outbox dequeue (locking semantics), any direct writes into NEXT3's own database, and bulk/reporting queries.

SQL Server outbox dequeue — `READPAST` is the equivalent of Postgres `SKIP LOCKED`:

```sql
UPDATE TOP (10) next3_outbox WITH (UPDLOCK, READPAST, ROWLOCK)
SET status = 'processing', attempts = attempts + 1
OUTPUT inserted.*
WHERE status = 'pending' AND next_retry_at <= SYSUTCDATETIME();
```

---

## 3. Architecture decisions — DECIDED

| Decision | Reasoning |
|---|---|
| **NEXT3 is the system of record for photos; the app holds files for days only** | Storage stays ~11 GB flat forever instead of growing ~68 GB/month. Requires a cleanup job and, critically, **never delete a blob before the NEXT3 push is confirmed sent**. |
| **Azure Blob, not Cloudflare R2** | R2 was recommended for zero egress fees — that mattered only while the app might be the permanent archive. As a days-only transit buffer, monthly egress (~68 GB) sits under Azure's 100 GB free allowance, so **R2's advantage is $0**. Meanwhile R2 would add a second vendor to clear through AXA procurement, a separate data-residency answer, and a second bill. *Lesson recorded: an architecture decision is only right relative to your other decisions — re-run the reasoning when an input changes.* |
| **Async processing via a transactional outbox** — not synchronous | Users are at accident scenes on bad connections; NEXT3 is a legacy core system with outages and maintenance windows. Sync means **NEXT3 down = experts cannot work**. Async means the queue drains on recovery and nobody notices. |
| **Container Apps with `minReplicas: 1`** | Scale-to-zero causes multi-second cold starts — unacceptable when an expert taps a claim notification at a crash site. ~$20–30/month, still well under App Service. Keep scale-to-zero for dev/UAT. |
| **Single tenant** | One client, he owns the software. No tenant isolation, no tenant-scoped queries, config in env vars not the database. Take this simplification everywhere. |

### The outbox pattern (core of the integration)

```
next3_outbox                       -- as built, slice 2.2; design.md §4 is the current contract
  id            uniqueidentifier
  visa_no       nvarchar(50)    -- was claim_id uniqueidentifier; there is no such id in the model
  operation     'upload_document' | 'update_arrival' | 'push_approval'
  payload       nvarchar(max)   -- JSON: blob keys, field values
  status        'pending' | 'processing' | 'sent' | 'failed'
  attempts      int
  last_error    nvarchar(max)
  next_retry_at datetime2
  created_at    datetime2
  sent_at       datetime2
```

Write the document row and the outbox row **in one transaction** — if they can't commit together you get documents never pushed, or pushes for documents that don't exist. Both surface weeks later as "AXA is missing photos", i.e. the exact problem this project exists to solve.

Worker loop: pick `pending` where `next_retry_at <= now` → push → success marks `sent`; failure increments `attempts`, records `last_error`, backs off (1min, 5min, 30min, 2hr…), and after ~8 attempts marks `failed`.

**Two things that are the classic bugs here:**
- **Make the push idempotent.** A retry after a timeout that actually succeeded must not duplicate the document in NEXT3. Send a stable `clientRef` with every push (open question — does NEXT3 dedupe on it?).
- **Never delete a blob before `status='sent'`.** Obvious, and reliably broken during a week-6 "cleanup" refactor.

**Build one admin screen listing failed pushes with a Retry button.** ~4 hours. It's the safety net, the debugging tool, the support answer, and a feature AXA will value more than half the BRD.

---

## 4. Frontend: PWA + Capacitor — the open research item

**The BRD asks for a mobile app, explicitly and four times:**

> *"a mobile application that can be **installed on mobile** or logged in PC"*
> *"Ability to use the user in a **mobile application or in browser**"*
> *"a mobile application that will be **installed on AXA experts network mobile**"*
> *"an invitation link is to be sent to his mobile number to **install the application**"*

**Why not a bare PWA.** A PWA is genuinely installable (home-screen icon, fullscreen, own splash) and skipping the app stores is arguably an advantage for a controlled internal network. But on **iOS**: Add-to-Home-Screen is a manual Safari-only three-tap flow that non-technical field users will not reliably complete, **and iOS web push only works after that install**. The BRD's entire expert flow is triggered by *"a popup message will show on the expert mobile"* — so an iPhone user who never installs simply never gets claims. That's a functional failure of the primary requirement, not a cosmetic gap.

**Why not Flutter.** React web + Flutter mobile = three codebases (.NET API, React, Flutter) and four languages, with every profile built twice. Adds an estimated **3–4 weeks** — turning 8 weeks into 11–12, unpaid at a fixed $5,000. Its real advantages (camera control, background push reliability) don't justify that here.

**Decision: React PWA wrapped in Capacitor.** One React codebase serves installed mobile app, desktop browser, and PWA fallback. Gets real store presence, native APNs/FCM push, and native camera/geolocation, for an estimated **1–1.5 weeks** instead of 3–4.

| Approach | Extra time | Codebases | Store presence | iOS push |
|---|---|---|---|---|
| PWA only | 0 | 1 | No | Fragile |
| **PWA + Capacitor** | **~1–1.5 wks** | **1 (+ thin shell)** | **Yes** | **Native, reliable** |
| Flutter native | 3–4 wks | 2 | Yes | Native, reliable |

Flutter is **deferred, not rejected** — revisit with evidence at the week-6 device-testing checkpoint if push or camera prove inadequate.

Known remaining costs: Apple Developer account ($99/yr, AXA's), Google Play ($25 one-off), and store review cycles. **The Mac question is largely answered (2026-08-18): Codemagic gives 500 free macOS M2 build minutes/month** — enough for this project — but *only on a personal account*, not a Team account, so the CI account stays in the developer's name while the store accounts are AXA's. Overflow is $0.10/min. Developer may also acquire a MacBook, and **owns an iPhone 17 Pro Max**, so real-device iOS testing at the week-6 checkpoint is covered at zero cost. §7 item 7 is therefore de-risked, not eliminated — still confirm the Capacitor iOS build actually runs on Codemagic's free tier before relying on it. **Not mentioned to the client: this costs AXA nothing.**

---

## 5. Commercial position

- **$5k / 2 months is already committed.** Independent estimates for the full BRD: ~300–430 person-days with a team, or 9–13 months solo. The commitment stands; the strategy is **scope control, not renegotiation** — don't spend the user's time re-deriving that gap.
- Roughly **60–70% of the BRD** fits the timeframe. Exclusions in `docs/scope-decisions.md` must be agreed **in writing before code starts**.
- **2026-08-19: Broker Option 2 moved INTO scope** by the developer, knowing it costs ~1.5–2.5 weeks and was the primary negotiating chip. The chip is spent — the remaining give is offline mode (already out), damage-diagram polish, and the second UAT round. Say so early if the date moves.
- **IP ownership is unresolved and worth money.** "He owns the software" would transfer full IP for $5,000. Propose instead: AXA gets full source, a perpetual unlimited licence, and the right to modify — developer retains the right to reuse **generic, non-AXA-specific components** (auth, upload pipeline, outbox) in future work. AXA loses nothing they care about; that reuse is worth more than this contract. If they insist on full transfer, price it rather than give it away silently.
- Payment: **40% up front / 30% at week-4 demo / 30% at handover.**
- ~~Two UAT rounds~~ **One UAT round** (2026-08-19 — the second round was spent to fund Broker Option 2; see design.md §11), tightly defined and capped in writing.
- Define "handover complete": source in their repo, deployment runbook, account ownership transferred, one training session. Otherwise "he owns it" becomes unpaid support forever.
- All third-party costs (SMS, hosting, domain, developer accounts) on **AXA accounts, AXA card**.

---

## 6. NEXT3 — what we actually know

**Searched the web on 2026-08-18: NEXT3 has no meaningful public presence as a commercial insurance product.** Results returned generic core-system vendors (Majesco, Sapiens, FINEOS) and the unrelated US insurer "NEXT Insurance". Not proof — a small regional vendor can have near-zero footprint — but the BRD's own wording is stronger evidence:

> *"To have **the possibility** to integrate the pictures … in NEXT3"* — they list as a prerequisite whether integration is even *possible*
> *"To **identify the directory** to upload photos and **insert records**"* — file share + direct DB writes, not a product API
> *"To have **visibility on fields** that will be updated"* — schema-level thinking
> *"To **extract** the data of experts … and **provide this list**"* — a manual export

**Working conclusion: bespoke or heavily customised, no API-first design.** So *"they will provide API endpoints"* most likely means **someone will build endpoints for you** — which makes NEXT3 availability the **#1 schedule risk, above InfoSec**.

**Highest-leverage action available: write the API contract yourself.** Don't wait to receive a spec. Send an OpenAPI document defining the ~8 endpoints needed:

| Endpoint | Purpose |
|---|---|
| `POST /auth/token` | Service authentication |
| `GET /claims/{visaNo}` | Claim details (visa, policy, plate, insured name + phone, car make/model, city, accident date) |
| `GET /claims/search?plateNo=&visaNo=` | Expert's "search previous claims" |
| `POST /claims/{visaNo}/arrival` | Arrival date, time, lat/lng |
| `POST /claims/{visaNo}/documents` | Multipart + `docType`, `folder` (Expert documents / Survey), **`clientRef` for idempotency** |
| `GET /experts` | Master-data sync (id, name, phone, active) |
| `GET /visas/search` | Claim officer's visa lookup |
| **Assignment delivery** | **Genuine hole in the BRD** — it says a *"back-office engine triggers the process"* without saying how that reaches the app. Webhook from NEXT3 (preferred) or the app polls. Decide it. |

Sending this converts a vague promise into a reviewable, datable deliverable; gets the endpoints built to fit the app; and makes any variance visibly theirs.

**Architectural protection already in place:** the NEXT3 client sits behind an interface with a working fake implementation, so all four modules can be built while AXA is still deciding. That covers roughly through week 3 — beyond that the date needs renegotiating, and say so early.

---

## 7. ⏭️ NEXT SESSION — write the design document

**Deliverable: `docs/design.md`.** An *internal engineering* design document — the thing you build from. Not client-facing, so it can be blunt about unknowns and risk. A client-facing summary can be extracted from it later if AXA asks.

**Why this now:** the BRD says *what*, `scope-decisions.md` says *how much*, and nothing yet says *how*. Four modules, four roles, a legacy integration and a public capture surface do not fit in one head; the first week of coding will otherwise be spent making architecture decisions ad hoc, in feature code, under time pressure.

### What it must contain

1. **Scope restatement** — one page. In scope, out of scope, and the simplifications from `docs/scope-decisions.md`. **Broker Option 2 is now in scope** (decided 2026-08-19).
2. **Roles and permissions matrix** — Expert, Garage, Claim Officer, Broker, **Admin** (implied by the BRD, never listed as a profile, still real work), and the **unauthenticated customer** in Broker Option 2. Every screen mapped to who may open it.
3. **System architecture** — components and their boundaries: React app (installed + browser), .NET API, background worker, Azure SQL, Blob, NEXT3 client behind its interface, email sender, push sender, SMS sender. Show which components the public Option 2 surface may and may not touch.
4. **Data model** — tables with columns and keys. At minimum: profiles for the four types, users/auth, claim (cached NEXT3 detail), document, `next3_outbox` (already specified in §3), broker_request (+ its Option 2 link token), notification, audit log. Say explicitly which data is a cache of NEXT3 and which is ours and authoritative.
5. **Module designs** — per module: screen inventory, the state machine, and what each transition writes. Treat **Garage + Claim Officer as ONE state machine with two views**, not two modules; the notifications between them are the part people forget to budget.
6. **NEXT3 integration design** — the eight operations from `docs/client-doc-src.html` §3.1, the outbox, idempotency via `clientRef`, retry/backoff schedule, the failed-push admin screen, and how the fake implementation stays usable all the way to handover.
7. **Media pipeline** — capture-only enforcement for car-photo buckets, the resolution + blur-variance quality check, blob lifecycle, and the rule that **no blob is deleted before its push is `sent`**.
8. **Notifications** — push (assignment popup, approval/rejection), SMS (OTP + invite links + the Option 2 customer link), and the broker email routing table keyed by insurance type.
9. **Security and data protection** — auth model, role enforcement, audit trail, and a dedicated subsection for **Broker Option 2**: token generation, expiry, single-use vs reusable, what the page exposes before submission, and rate limiting. This is the project's largest attack surface and the thing most likely to attract AXA Group InfoSec.
10. **Environments** — one or two (test + production). Recommendation on record is two.
11. **Revised week-by-week plan** — `docs/estimate-and-plan.md` is an 8-week plan that predates Option 2. Re-cut it with Option 2 included and say plainly what moved or dropped to make room.
12. **Design decisions blocked on client answers** — which of the 41 open questions block which design choices, and the placeholder strategy for each (one config file, obvious fake values, never invented client data).

### Rules for the session writing it

- **Do not invent client data.** Insurance types, email recipients, NEXT3 field names, document-type codes and IRIS codes are all unanswered. Placeholders only, all in one config file, clearly marked.
- **Prefer the smaller interpretation** of anything ambiguous, and record it in `scope-decisions.md` rather than silently designing the bigger thing.
- **Design against the interface, not against AXA's availability.** The NEXT3 client is an interface with a working fake; nothing in the design may assume the real endpoints exist.
- Where a decision is genuinely open, **write the recommendation and the reason**, not a list of options. This document exists to stop decisions being re-made.

### Inputs to read first

`docs/BRD-extracted-text.md` (the source of truth), `docs/diagrams/` (four flowcharts — broker Option 1 and 2 are diagrams 3 and 4), `docs/scope-decisions.md`, `docs/open-questions.md`, and §§ 2–6 of this file.

### Explicitly NOT in this deliverable

Visual design, mockups, branding, colour. AXA supplied none, and it is not in scope. Screen *inventories* and *flows* yes; pixels no.

---

## 7A. Deferred — research brief: PWA + Capacitor

**Goal:** validate or kill the PWA + Capacitor decision in §4 **before** week 1 coding starts, and produce a concrete build/distribution plan. **Deferred behind the design doc (§7), but still needed before coding** — the design doc should record the Capacitor choice as provisional and flag anything that depends on it.

### Must answer

1. **Capacitor + React current setup** — current major version, project structure alongside a React/Vite app, how the web build feeds the native shell, what the dev loop looks like (live reload on device).
2. **iOS push via Capacitor** — APNs certificate/key provisioning, what an Apple Developer account must have configured, whether it works reliably when the app is backgrounded or killed. **This is the single most important question** — the BRD's expert flow depends entirely on the popup arriving.
3. **Android push** — FCM setup, and specifically **whether aggressive-battery-saver OEMs (Xiaomi, Huawei, Oppo, Samsung) kill background delivery.** Common in MENA. Find the known mitigations.
4. **Camera: forcing capture, blocking gallery upload.** The BRD requires car photos be **capture-only** (`Insured Car Photo`, `TP Car Photo`, garage car photos) while document buckets allow both. Confirm the Capacitor Camera plugin can enforce camera-only, on both platforms.
5. **Voice recording** — which plugin, what formats, file sizes, iOS permission behaviour.
6. **Geolocation** for the "Arrived" button — accuracy, permission prompts, behaviour when denied.
7. **Building iOS without a Mac** — evaluate Codemagic, Bitrise, Ionic Appflow. Free tiers, paid tiers, monthly cost, setup effort. **This is a hard blocker if unsolved.**
8. **Distribution** — public App Store / Play Store vs enterprise/MDM internal distribution. Which suits a controlled network of experts and garages? What does each require from AXA?
9. **Client-side image quality check** — a resolution + blur-variance (Laplacian) check in the browser/webview, to implement the BRD's *"image visibility must be ensured"*. Find a workable approach and a sane threshold. **Explicitly not ML.**
10. **Sanity-check the alternatives** — is Capacitor still the right pick in 2026 versus a bare PWA or a React Native rewrite? Look for anything that has changed.

### Also confirm

- PWA fallback quality for the desktop/browser users (claim officer, broker, admin) from the same codebase
- Offline behaviour available "for free" — deferred as scope, but worth knowing what comes at no cost
- Realistic revised estimate for the Capacitor wrapper: is 1–1.5 weeks right?

### Deliverable

Write findings to **`docs/research-capacitor.md`**, ending with a clear **go / no-go** on PWA + Capacitor and, if go, a concrete build-and-distribution plan slotted into week 6 of the plan in `docs/estimate-and-plan.md`.

### Blocked on the client for

Questions **28, 29, 30** in `docs/open-questions.md` — device mix (iOS vs Android share across the expert/garage network), store vs MDM distribution, and whether AXA already holds an Apple Developer account. **If the network is 80%+ Android** (plausible in MENA), the iOS risk shrinks sharply and Android-only Capacitor needs no Mac at all — which would materially simplify everything above. **Chase these answers in parallel; don't block the research on them.**

---

## 8. Immediate next actions (non-coding, still outstanding)

1. **Send the blocking questions** — the 8 in `docs/open-questions.md` § Blocking, plus #21 (InfoSec/pen test), #23 (PWA acceptable), #28–30 (devices). Prioritise **#1, #2, #21, #31** — those decide whether 2 months is real.
2. **Send the one-page scope letter** with the exclusions from `docs/scope-decisions.md` and dated client dependencies. Email acknowledgement is sufficient. *Still not drafted.* **Must now state that Broker Option 2 IS included** — it is the biggest thing you are giving them, so do not give it away silently.
3. **Send the NEXT3 OpenAPI proposal** (§6). Fastest way to force clarity on the biggest unknown.
4. **Agree payment milestones and the IP position** (§5).
5. **Confirm all infra accounts are in AXA's name, on AXA's card.**
6. Then scaffold — week 1 of `docs/estimate-and-plan.md`.

**Say out loud to the client now, not later:** every day the NEXT3 sandbox credentials slip, the delivery date moves day-for-day.

---

## 9. Context an AI session won't infer

- **No client answers have been received.** Every TBC is genuinely unknown — **do not invent** insurance types, email routing recipients, NEXT3 field names, or document-type codes.
- Client contact who last edited the BRD: **HADDAD Ramy**, AXA Middle East. Document created 2026-08-05.
- The three things most likely to break the 2 months: **NEXT3 endpoints not existing yet**, **AXA Group InfoSec review / pen test**, and **UAT scope creep in weeks 7–8**.
- The developer is deliberately building cost-estimation skill — cost reasoning is welcome, and decisions should be explained in terms of unit economics (cost per claim) and total cost of ownership including his own time (~$30/hr effective on this contract), not sticker price.

---

## 10. Repo map

```
docs/
  BRD-extracted-text.md     Full text extracted from the client .docx
  source/                   Original client .docx (unmodified)
  diagrams/                 The client's 4 flowcharts as PNG
  scope-decisions.md        In scope / out of scope / simplifications
  open-questions.md         40 questions for the client, prioritised
  estimate-and-plan.md      8-week plan, hosting options, monthly cost
  client-doc-src.html       SOURCE of the client Word document - edit content HERE
  build-docx.ps1            Regenerates the .docx from that HTML. Never hand-edit the .docx:
                            LibreOffice's HTML import breaks table widths, margins and the
                            Word-version stamp, and this script patches all of it. Re-run as
                            `pwsh -File docs\build-docx.ps1 -Version 0.3`
  AXA-Motor-Claims-Questions-and-Costs-v0.4.docx   The client document (4pp), NOT yet sent
                            v0.4 = client's own heavy cut of v0.3, folded back into the HTML
                            source. Now covers ONLY: purpose, what's needed at a glance, NEXT3
                            endpoints + 9 questions, and hosting/SMS/mobile cost. Client deleted
                            the title block, the schedule statement, connectivity options, the
                            OpenAPI note, the SMS provider options, and all of security /
                            mobile delivery / remaining questions / next steps. Those cuts are
                            deliberate - do not restore them without asking.
  client-email-2026-08-18.md  Covering email to Ramy, NOT yet sent
  design.md                 Internal engineering design (§7) — WRITTEN 2026-08-19. The build contract.
  build-playbook.md         ~25 ordered build slices with per-session prompts + milestone checklists — written 2026-08-19.
                            Progress ticks + per-slice Notes live here (1.1–1.3 done as of 2026-08-19)
  research-claude-workflow.md  How to drive the build with Claude Code — written 2026-08-19
  research-capacitor.md     ← still unwritten (§7A). Validates the Capacitor decision; needed before week 6
AxaMotorClaims.sln          Week-1 solution (slices 1.1–1.3)
src/
  Api/                      .NET 10 minimal API. Modules/Users (auth, profiles, admin CRUD),
                            Modules/Audit (append-only audit_log + AuditWriter), Modules/Notifications
                            (notification log + NotificationLog writer), Modules/Claims (the `claim`
                            NEXT3 cache + ClaimCache, §4's refresh-on-open rule — shared with the
                            officer's lookup in 4.2), Modules/Expert (expert_assignment, the single
                            idempotent AssignmentHandler + its startup subscription, E1/E2 read
                            endpoints, the Arrived endpoint — the codebase's only explicit
                            transaction, because ExecuteUpdate's WHERE clause is what makes an
                            arrival happen exactly once — admin dev-injection endpoint),
                            Modules/Broker (broker_request
                            + B3 create-link), Modules/PublicSurface (the ONLY unauthenticated surface:
                            public_link_token, /public/* endpoints, chained per-IP/per-token rate
                            limiter, body-size cap — may not reference Users or Next3, arch rule 2),
                            Integrations/ (the 5 ports + fakes: Next3 incl. IAssignmentSource, Email,
                            Push, Sms; FakeBehavior = shared latency/failure injection),
                            Modules/Media (§7's pipeline: the `document` table, MediaBuckets = §7.1's
                            matrix as data, ImageHeader + MediaValidation = §7.2 item 5's server
                            re-validation, MediaUploadService = the streamed multipart endpoint's
                            blob-then-rows ordering, MediaBlobCleanupTask = §7.3's two sweeps),
                            Infrastructure/Cleanup (ICleanupTask + CleanupRunner + CleanupWorker,
                            schedule split from behaviour like the outbox worker),
                            Integrations/Blob (the sixth port: IBlobStore + InMemoryBlobStore fake +
                            AzureBlobStore; Blob:Mode = fake | azure, fake everywhere by default),
                            Outbox/ (next3_outbox + the READPAST dequeue proc, OutboxWriter = the
                            one-transaction enqueue every producer uses, OutboxProcessor = the §6.3
                            retry/backoff loop body, OutboxWorker = its schedule; the ONLY namespace
                            that may call INext3Client push ops or touch outbox rows, arch rules 3+4),
                            appsettings.Placeholders.json (Appendix A — ALL client-value placeholders)
  Api.Tests/                xUnit: NetArchTest boundary rules (with planted-violation self-tests) +
                            integration tests on LocalDB via WebApplicationFactory (283 tests;
                            READ_COMMITTED_SNAPSHOT is switched on to match Azure SQL).
                            Media/ holds TestImages (synthetic JPEG/PNG/PDF headers) and the
                            IBlobStore contract tests, whose Azurite half self-skips when the
                            emulator is not listening — so the suite needs no Docker
  Web/                      React 19 + TS + Vite. Router, localStorage JWT + refresh-on-401 client,
                            admin pages (login, per-kind profile list/form). Dev proxy → API :5180.
                            Slice 2.4 added: TanStack Query (api/queryClient.ts), role-based
                            post-login routing (api/session.ts — navigation only, server still
                            enforces), UTC normalisation for bare API timestamps (api/datetime.ts),
                            expert/ (api + query hooks + geolocation + useArrived — all E1/E2 logic),
                            pages/ExpertAssignments*.tsx (E1/E2), and a Vitest suite (`npm test`,
                            20 tests; testing/setup.ts registers @testing-library's cleanup by hand)
.claude/
  settings.json             Hook registrations (post-edit format check, on-stop test run)
  hooks/                    Guarded PowerShell hooks — no-op until the solution exists in week 1
  skills/scaffold-module/   Skill: scaffold an API module per design.md §5 pattern
  skills/add-ef-migration/  Skill: add + safety-review + apply an EF Core migration
  agents/db-reviewer.md     Read-only subagent auditing EF migrations
CLAUDE.md                   Domain glossary + conventions + design.md import (the per-session contract)
HANDOFF.md                  This file
```
