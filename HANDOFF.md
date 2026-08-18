# HANDOFF — read this first

**Project:** AXA Middle East — Mobile Application for Motor Claim Management
**Developer:** solo (Ali Sleiman)
**Commitment:** 2 months, $5,000 fixed, developer handles everything
**Status as of 2026-08-18:** Pre-kickoff. BRD analysed, stack and architecture decided. **No code written. No client answers received.**

> ## ⏭️ NEXT SESSION: research PWA + Capacitor
> Jump to **§ 7 — Next session research brief** at the bottom. Everything above it is context for that work.

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
next3_outbox
  id            uniqueidentifier
  claim_id      uniqueidentifier
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

Known remaining costs: Apple Developer account ($99/yr, AXA's), Google Play ($25 one-off), store review cycles, and **a Mac for iOS builds** — cloud macOS CI (Codemagic / Bitrise) is the likely answer. **All of this is what §7 needs to verify.**

---

## 5. Commercial position

- **$5k / 2 months is already committed.** Independent estimates for the full BRD: ~300–430 person-days with a team, or 9–13 months solo. The commitment stands; the strategy is **scope control, not renegotiation** — don't spend the user's time re-deriving that gap.
- Roughly **60–70% of the BRD** fits the timeframe. Exclusions in `docs/scope-decisions.md` must be agreed **in writing before code starts**.
- **IP ownership is unresolved and worth money.** "He owns the software" would transfer full IP for $5,000. Propose instead: AXA gets full source, a perpetual unlimited licence, and the right to modify — developer retains the right to reuse **generic, non-AXA-specific components** (auth, upload pipeline, outbox) in future work. AXA loses nothing they care about; that reuse is worth more than this contract. If they insist on full transfer, price it rather than give it away silently.
- Payment: **40% up front / 30% at week-4 demo / 30% at handover.**
- **Two UAT rounds**, capped in writing.
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

## 7. ⏭️ NEXT SESSION — research brief: PWA + Capacitor

**Goal:** validate or kill the PWA + Capacitor decision in §4 **before** week 1 coding starts, and produce a concrete build/distribution plan. This decision underpins the whole 8-week schedule, so it is the right thing to de-risk first.

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
2. **Send the one-page scope letter** with the exclusions from `docs/scope-decisions.md` and dated client dependencies. Email acknowledgement is sufficient. *Still not drafted.*
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
  open-questions.md         31 questions for the client, prioritised
  estimate-and-plan.md      8-week plan, hosting options, monthly cost
  research-capacitor.md     ← to be written next session (§7)
CLAUDE.md                   Domain glossary + conventions
HANDOFF.md                  This file
```
