# design.md — Internal engineering design

**Project:** AXA Middle East — Mobile Application for Motor Claim Management
**Status:** working design, 2026-08-19. Internal document — blunt about unknowns by design. Not client-facing; a client summary can be extracted on request.
**Inputs:** `BRD-extracted-text.md` (source of truth), `diagrams/` (the client's four flowcharts — load-bearing, they contain rules the prose lacks), `scope-decisions.md`, `open-questions.md`, `client-doc-src.html` §3, `HANDOFF.md` §§2–6.
**How to read this:** every decision is written as a recommendation with its reason, not a list of options — this document exists to stop decisions being re-made. Anything unresolved is flagged `#N` (an open-question number) and resolves to a named key in the placeholder config (Appendix A). No client data is invented anywhere in this document.

Section numbering 1–12 matches HANDOFF §7's required contents exactly, so completeness can be checked mechanically.

---

## 1. Scope restatement

Scope status: proposed by the developer, **not yet acknowledged by the client** — including Broker Option 2, which was added at the developer's decision on 2026-08-19. The scope letter must present all of this for written acknowledgement before code starts.

### In scope (8 calendar weeks — held at 8, see §11 for what paid for that)

| Item | Content |
|---|---|
| Admin + onboarding | CRUD for all 4 profile types, invite links by SMS, phone-OTP login, active/inactive, NEXT3 ID mapping |
| Expert module (full) | Assignment popup → claim detail → Arrived (date/time/location to NEXT3) → voice note, capture-only car photos in 4 buckets, car body damage diagram (functional, unpolished — see §11), clarity gate with retry, search by visa/plate, expert report upload |
| Garage + Claim Officer survey flow (full) | One declaration state machine with two views (§5.2): declaration, officer review, visa search, approve/reject with comments, approval rendered to PNG into the NEXT3 Survey folder, notifications both ways, post-repair uploads |
| Broker Option 1 | 6-field form + document upload/capture with provenance flag and a configurable upload kill-switch, email routed by insurance type |
| Broker Option 2 | Broker sends a link to the customer's mobile; unauthenticated customer completes the same 6 fields, uploads supporting documents, captures 4 car sides + roof with side selection and clarity check; file returns to the broker as *ready to send*; broker reviews and triggers the email. **In scope as of 2026-08-19.** |
| NEXT3 integration | The 7 operations + sandbox of §6, via transactional outbox, behind an interface with a working fake |

### Out of scope (must be excluded in writing)

| Excluded | Note |
|---|---|
| Offline mode | Not in the BRD; genuinely needed roadside — flagged as a known limitation, proposed as phase 2 |
| Arabic / RTL, French | English only unless separately funded |
| Production SLA / 24-7 support | Defined hypercare window instead |
| Reporting / MIS / dashboards | Not in the BRD; will be asked for |
| Voice transcription | Record, upload, store — nothing more |
| Damage-diagram polish | **Dropped 2026-08-19** to hold 8 weeks with Option 2 in (see §11). Diagram ships functional: static SVG, tap-to-mark, PNG export |
| Second UAT round | **Dropped 2026-08-19**, same trade (see §11). One UAT round, tightly defined |

**Superseded:** the earlier scope line "Native app store builds out — deliverable is a PWA" is stale. The decided delivery (HANDOFF §4) is the single React codebase shipped three ways: Capacitor-wrapped mobile app (store or MDM — #29), browser web app for desktop roles, PWA as fallback. The Capacitor choice is **provisional pending `research-capacitor.md`** (unwritten) and the week-6 real-device checkpoint; #23's "confirm PWA acceptable" question is likewise superseded by "confirm the delivery model", and `scope-decisions.md` is updated to match.

### Simplifications (interpretations of vague BRD text — each must appear in the scope letter)

| BRD text | Interpretation |
|---|---|
| *"Image visibility and voice clarity must be ensured"* | Client-side resolution floor + blur-variance threshold + user confirm screen. Voice = user playback-confirm only, no signal analysis. **Not ML.** (#9) |
| *"a car body diagram where the expert can mark the accident spot"* | Static SVG car, tap-to-mark hotspots, flattened to PNG via canvas (#11) |
| *"approval and comments is to be captured as an image"* | Render the comment block to canvas → PNG → push to NEXT3 (#18) |
| *"immediate or once per day"* (sync mode) | **Immediate** (#17) |
| Rejection handling (BRD: "Disregard Case", no resubmit path) | Rejection is **terminal**; the garage files a new declaration if needed. No edit-after-reject semantics. No NEXT3 push on rejection (matches diagram 02, which pushes only on approval) |
| Declaration lifecycle end | Ends at *repair documents submitted*. No closure/settlement state — the BRD defines none and we do not invent one |
| Garage declaration form fields (BRD defines none) | **Plate number (required)** — it is the claim-search key the officer needs — plus optional insured name and free-text note. Nothing more |
| Expert "done" signal (BRD defines none) | None built. An assignment accumulates media indefinitely; there is no submit/complete transition on the expert side |
| Option 2 Estimated Premium | **Kept as the BRD writes it: the customer enters it** (developer decision 2026-08-19, after flagging that customers pricing their own insurance looks wrong). Flagged on #24c; if AXA moves it broker-side, the change is form-level only |

---

## 2. Roles and permissions matrix

Six actors. **Admin is implied by the BRD** (it names an "application admin" as the creator of profiles) **but never listed as a profile** — it is real work and is designed here. The **unauthenticated customer** exists only in Broker Option 2 and touches exactly one surface.

Screen inventory (defined per module in §5):

| ID | Screen | Expert | Garage | Officer | Broker | Admin | Public customer |
|---|---|---|---|---|---|---|---|
| S1 | Invite landing / registration + OTP verify | ● | ● | ● | ● | — | — |
| S2 | Login (phone + OTP) | ● | ● | ● | ● | ● | — |
| E1 | Claim list + search (visa/plate) | ● | — | — | — | — | — |
| E2 | Claim detail (Arrived button) | ● | — | — | — | — | — |
| E3 | Media capture (4 buckets, voice, damage diagram) | ● | — | — | — | — | — |
| E4 | Clarity review / confirm | ● | ● | — | ● | — | ● |
| E5 | Expert report upload | ● | — | — | — | — | — |
| G1 | Declaration worklist | — | ● | — | — | — | — |
| G2 | New declaration (form + uploads) | — | ● | — | — | — | — |
| G3 | Declaration detail (state-dependent) | — | ● | — | — | — | — |
| G4 | Post-repair uploads | — | ● | — | — | — | — |
| O1 | Declaration inbox | — | — | ● | — | — | — |
| O2 | Declaration review (visa search, approve/reject) | — | — | ● | — | — | — |
| B1 | Request list (incl. *ready to send*) | — | — | — | ● | — | — |
| B2 | New request — Option 1 form | — | — | — | ● | — | — |
| B3 | Create customer link — Option 2 | — | — | — | ● | — | — |
| B4 | Request review + Send Email | — | — | — | ● | — | — |
| P1 | Public customer page (Option 2, token URL) | — | — | — | — | — | ● |
| A1 | Profile management (4 CRUDs + invites) | — | — | — | — | ● | — |
| A2 | Failed-push queue + Retry | — | — | — | — | ● | — |

Notes:
- E4 (clarity confirm) is one shared component; it appears wherever media enters the system, per the BRD's "this step is to be added … to all profile steps".
- Every authenticated screen is role-gated server-side (§9); the grid above is the authorization policy, not just navigation.
- Admin has no mobile requirement; A1/A2 are browser screens.
- The public customer sees P1 and nothing else; P1 is served from the public surface only (§3).

---

## 3. System architecture

```
                             ┌────────────────────────────────────────────┐
                             │                 React app                  │
   Expert / Garage (mobile)  │  one codebase, three shells:               │  Officer / Broker / Admin
   ── Capacitor shell ─────► │  Capacitor (iOS/Android) · browser · PWA   │ ◄──── browser ────
                             └─────────────────┬──────────────────────────┘
                                               │ HTTPS (JWT)
        public customer (no auth)              ▼
        ───────────────► /public/* ──► ┌──────────────────┐        ┌─────────────────────┐
                        (token URL,    │    .NET 10 API   │ ─────► │  Azure SQL Database  │
                         rate-limited) │  Container Apps  │        │  (EF Core + targeted │
                                       │  minReplicas: 1  │        │   stored procs)      │
                                       └──┬───────┬───────┘        └──────────┬───────────┘
                                          │       │                           │ outbox rows
                            direct upload │       │ senders                   ▼
                                          ▼       │            ┌────────────────────────────┐
                                 ┌────────────┐   │            │  Outbox worker             │
                                 │ Azure Blob │   │            │  (Container Apps Job)      │
                                 │ transit    │◄──┼── reads ───│  dequeue READPAST → push   │
                                 │ buffer only│   │            └──────────┬─────────────────┘
                                 └────────────┘   │                       │
                                                  ▼                       ▼
                                     ┌─────────────────────┐   ┌─────────────────────────┐
                                     │ IEmailSender        │   │ INext3Client (interface)│
                                     │ IPushSender         │   │  ├─ FakeNext3Client     │
                                     │ ISmsSender          │   │  └─ RealNext3Client ────┼──► NEXT3
                                     └─────────────────────┘   │ IAssignmentSource       │◄─── webhook
                                                               └─────────────────────────┘     (or poll)
```

Components and boundaries:

- **React app** — one TypeScript codebase. Capacitor shell for Expert/Garage field users (native push, camera, geolocation); plain browser for Officer/Broker/Admin desktops; PWA as fallback. Capacitor specifics are provisional pending `research-capacitor.md`.
- **.NET 10 API** — all business logic and authorization. Containerized on Azure Container Apps, `minReplicas: 1` in production (cold starts are unacceptable when an expert taps a claim popup at a crash site).
- **Outbox worker** — Container Apps Job running the outbox loop (§6). The only component that talks to NEXT3 for writes.
- **Azure SQL** — the application database. EF Core migrations + LINQ for the domain; stored procedures only where they earn it: outbox dequeue, any direct NEXT3-DB writes (if #5 answers "directory + DB"), bulk queries.
- **Azure Blob** — transit buffer only. NEXT3 is the system of record for media; blobs live for days, not forever (§7).
- **NEXT3 client** — `INext3Client` interface with `FakeNext3Client` (default everywhere until the sandbox exists) and `RealNext3Client`. Nothing outside this boundary knows which is active. `IAssignmentSource` handles the inbound direction (§6).
- **Senders** — `IEmailSender` (broker routing), `IPushSender` (assignment/decision popups), `ISmsSender` (OTP, invites, Option 2 link if #24a says SMS). Each has a fake for dev/demo.

### The public surface (Broker Option 2)

The `/public/*` endpoint group is the only unauthenticated surface. Hard boundary, enforced in code structure (separate controller group + separate authorization policy), not convention:

**May touch:** `public_link_token` (validate/lock), `broker_request` (the one row its token points at), `document` rows it creates, Blob upload (SAS-scoped to its own request's prefix), the clarity check.
**May not touch:** any authenticated API, `INext3Client` (the broker module never writes to NEXT3 at all), the `claim` cache, user/profile tables, admin functions, other brokers' or other tokens' data.

Rate limiting, token design, and what the page reveals: §9. WAF in front of this surface is AXA's cost call (#38) and is layered on top of, not instead of, these controls.

Single tenant throughout: one client, no tenant isolation, config in env vars not the database.

---

## 4. Data model

Conventions: `uniqueidentifier` PKs (client-generatable, sortable enough with sequential GUIDs), `datetime2` UTC timestamps, soft state columns as `nvarchar` check-constrained enums (readable in support queries). **Authoritative** = this app owns the truth. **Cache** = NEXT3 owns the truth; our copy is disposable.

| Table | Ownership | Purpose / key columns |
|---|---|---|
| `app_user` | Authoritative | One row per authenticated identity. `id`, `phone` (unique, E.164), `role` (expert/garage/claim_officer/broker/admin), `display_name`, `status` (invited/active/inactive), `inactivated_at`, `created_at` |
| `expert_profile` | **Hybrid** — seeded from NEXT3 `GET /experts`, app-authoritative for app fields | `user_id` FK, `next3_id` (nullable until mapped; filtered unique index — same on `garage_profile`; realized 2026-08-19, slice 1.3), `email`, `active`, `inactivated_at`. Seed/sync job matches on `next3_id`; NEXT3 wins on identity fields, app wins on app status (#8 decides one-time vs synced) |
| `garage_profile` | Authoritative | `user_id`, `contact_name`, `phone`, `mobile`, `email`, `next3_id`, `address`, `opening_hours` (nvarchar — display text, not structured), `active`, `inactivated_at` |
| `claim_officer_profile` | Authoritative | `user_id`, `next3_user`, `email` |
| `broker_profile` | Authoritative | `user_id`, `iris_code` (placeholder semantics — #15), `email` |
| `invite` | Authoritative | Onboarding links. `id`, `user_id`, `token_hash`, `expires_at`, `used_at` |
| `otp_challenge` | Authoritative | `id`, `phone`, `code_hash`, `expires_at`, `attempts`, `consumed_at`, `created_at` (drives the §9 resend throttle). TTL'd, purged by cleanup job |
| `refresh_token` | Authoritative | §9's refresh token, realized (added 2026-08-19, slice 1.2). `id`, `user_id` FK, `token_hash` (SHA-256; raw token never stored), `expires_at`, `created_at`, `revoked_at`. Rotated on every use; presenting an already-rotated token revokes **all** the user's refresh tokens; all revoked on deactivation |
| `claim` | **Cache of NEXT3** | Keyed `visa_no`. `policy_no`, `plate_no`, `insured_name`, `insured_phone`, `car_make_model`, `city`, `accident_date`, `fetched_at`. Refresh rule: re-fetch on open; if NEXT3 is down, serve stale with a staleness banner. Never edited locally; deletable at any time |
| `expert_assignment` | Authoritative (event sourced from NEXT3) | `id`, `visa_no`, `expert_user_id`, `next3_assignment_ref` (dedupe key — §6), `received_at`, `notified_at`, `opened_at`, `arrived_at`, `arrival_lat/lng`. Timestamps, not a state enum — the BRD defines no expert-side lifecycle and we do not invent one |
| `declaration` | Authoritative | The §5.2 state machine row. `id`, `garage_user_id`, `state` (draft/submitted/approved/rejected/repairs_in_progress/repair_docs_submitted), `plate_no`, `insured_name?`, `note?`, `visa_no?` (set at approval), `officer_user_id?`, `decided_at`, per-transition timestamps |
| `declaration_comment` | Authoritative | `declaration_id`, `author_user_id`, `body`, `created_at`. Officer comments; visible to garage **only when state = approved** (BRD grants comment visibility only on confirmation) |
| `document` | Authoritative (metadata; binary is transit-only) | Every media item in the system. `id`, `owner_kind` (assignment/declaration/broker_request), `owner_id` (polymorphic — no FK), `bucket` (§7 enum, check-constrained; adding one is a migration), `doc_type` (placeholder codes — #12; null for media that never reaches NEXT3), `origin` (captured/uploaded — the BRD's provenance flag, kept on every row, required by Broker Option 1), `clarity_result` (**`passed` \| `not_applicable` only** — §7.2 item 5 rejects a failure rather than storing one, and blur is client-side, so no server path can write `failed`), `blob_key`, `content_type`, `size_bytes`, `push_status` (**`queued` \| `n/a`** — n/a for broker docs; live push state belongs to the outbox row and is deliberately not denormalised here, because §7.3 deletes blobs on that state and two copies of it can disagree), `created_by` (null for the Option 2 public customer), `created_at`. **Two columns added 2026-08-20, slice 2.3:** `outbox_message_id uniqueidentifier?` — §7.3 requires the cleanup query to join on outbox `sent` *structurally*, and the only other key available is the `clientRef` inside the outbox row's JSON payload, which is a convention and unindexed; **no FK**, because a relationship would put `Next3OutboxMessage` into this module's model configuration and break architecture rule 4. And `blob_deleted_at datetime2?` — the sweep must be idempotent, and "have this photo's bytes been cleaned up?" is a support question. A **unique** filtered index on `outbox_message_id` is a safety property, not tidiness: one confirmed push must license the deletion of exactly one document's bytes. A check constraint pins the cross-column invariant (`queued` ⇔ an outbox row exists), since `queued` with no row is a document that is never pushed, never listed on A2 and never eligible for deletion |
| `broker_request` | Authoritative | Both options. `id`, `broker_user_id`, `option` (1/2), `state` (§5.3), `insured_name`, `insurance_type` (from placeholder list — #14), `insured_address`, `car_value`, `estimated_premium` (customer-entered in Option 2 per §1), `effective_date`, `customer_mobile?` (Option 2), `submitted_at`, `emailed_at`, `email_recipient` (resolved from routing placeholder — #13), `created_at` (realized 2026-08-19, slice 1.5 — every other table has one and B1 lists newest-first). **The six form fields are nullable**: an Option 2 row exists from `link_issued`, before the customer has entered anything, so completeness is a precondition of the submit transition (§5.3), not a column constraint |
| `public_link_token` | Authoritative | Option 2 token (§9). `id`, `broker_request_id`, `token_hash` (SHA-256 of the 256-bit token; raw token never stored), `expires_at`, `locked_at` (set on successful submission), `created_at`, `row_version` (SQL `rowversion`; realized 2026-08-19, slice 1.5 — §9.1's "each link accepts exactly one submission" cannot hold in application code, where reading `locked_at` and writing it are two steps that two simultaneous submissions both pass; the concurrency token makes the database the arbiter and the loser renders as the same uniform 404) |
| `next3_outbox` | Authoritative | The integration core (§6): `id uniqueidentifier`, **`visa_no nvarchar(50)`**, `operation 'upload_document'\|'update_arrival'\|'push_approval'`, `payload nvarchar(max)` (JSON: the `clientRef` plus blob keys and field values), `status 'pending'\|'processing'\|'sent'\|'failed'`, `attempts int`, `last_error nvarchar(max)`, `next_retry_at datetime2`, `created_at datetime2`, `sent_at datetime2`. Indexed `(status, next_retry_at)` for the dequeue and A2's failed list, and on `visa_no` for "why is this claim missing photos". **`claim_id uniqueidentifier` corrected to `visa_no` (realized 2026-08-20, slice 2.2)** — HANDOFF §3 spelled it `claim_id`, but no uniqueidentifier claim id exists anywhere in the model: `claim` is keyed on `visa_no`, and this row must hold no FK to it for the same reason `expert_assignment` holds none (the cache is disposable, and a NEXT3 outage that empties it must not take the queue with it). Every `INext3Client` push takes a visa number and §5.4's A2 displays "claim/visa", so the column now carries the value it is actually used for. **Must stay trigger-free**: §6.3's dequeue returns `OUTPUT inserted.*`, and EF abandons the OUTPUT clause on any table it knows carries a trigger |
| `notification` | Authoritative | Log of every push/SMS/email attempt. `id`, `channel` (push/sms/email), `recipient_user_id?`, `recipient_address`, `template`, `payload`, `status` (queued/sent/failed), `sent_at`, `error`, `created_at` (realized 2026-08-19, slice 1.4 — a `failed` row never sets `sent_at`, so without it a failure has no timestamp). Written only by `NotificationLog`, which commits its own transaction rather than joining the caller's: a send already happened externally and its record must not vanish with a later rollback. **`payload` is null for SMS by rule** — every SMS this app sends carries a live credential (OTP code, invite token), and §9 hashes those precisely so a DB leak yields no working logins; copying the body here would hand that back |
| `push_subscription` | Authoritative | **New table, realized 2026-08-21, slice 3.4** — §8's push rows had no table because until then the only sender wrote to the console. One row per browser per user, so an expert with a phone and a desk machine gets the popup on both. `id`, `user_id` (FK **Restrict** — a future hard delete must not take the record of which devices we notified), `endpoint` (nvarchar(2048)), **`endpoint_hash`** (SHA-256; the unique index cannot sit on the endpoint itself, because SQL Server caps an index key at 900 bytes — hashing here is for *length*, not secrecy, which is why the endpoint is stored beside it in the clear), `p256dh`, `auth` (the browser's own encryption keys, stored as given: they are useless without the endpoint, and the endpoint is useless without our VAPID private key, so unlike §9's tokens they grant nothing on their own), `created_at`, `last_used_at?`, `revoked_at?`. **Unique on `(user_id, endpoint_hash)`** — that index *is* the upsert (1.5's lesson, fifth time), and it is scoped to the user rather than global so two people sharing a browser profile each keep their own row. `revoked_at` is set when the push service answers 404/410, which is terminal; an explicit unsubscribe deletes instead |
| `audit_log` | Authoritative | `id`, `actor_user_id?` (null = public customer or system), `action`, `entity_kind`, `entity_id?` (null when the event has no entity, e.g. failed login for an unknown phone — realized 2026-08-19, slice 1.3), `detail` (JSON), `at`. Append-only, enforced by a DB trigger (`INSTEAD OF UPDATE, DELETE`), not convention; the only code write path is `AuditWriter.Append`, which joins the caller's transaction. The InfoSec answer to "who uploaded which photo, when" (§9) |

Cross-cutting rules:
- `document` + `next3_outbox` rows for the same media item are written **in one transaction** — if they can't commit together we get either documents never pushed or pushes for documents that don't exist, both of which surface weeks later as "AXA is missing photos", i.e. the exact problem this project exists to solve.
- Exactly one table is a NEXT3 cache (`claim`). Everything else is ours. Any future table gets classified in this section before it exists.
- Volumes for sizing are unanswered (#20); working assumption remains ~100 claims/day, ~15 photos × 1.5 MB, ~200 users.

---

## 5. Module designs

### 5.1 Expert

**Screens:** E1 claim list + search, E2 claim detail, E3 media capture, E4 clarity confirm, E5 report upload (see §2).

**Flow and writes:**

| Step | Trigger | Writes |
|---|---|---|
| Assignment received | `IAssignmentSource` event (§6) | `expert_assignment` row (dedupe on `next3_assignment_ref`), `claim` cache fetch, `notification` row, push to expert |
| Expert opens popup → E2 | tap | `expert_assignment.opened_at`, `audit_log` |
| **Arrived** | button on E2 | `arrived_at`, `arrival_lat/lng` on the assignment; **outbox row** `update_arrival` (date, time, GPS — field names/formats are #6 placeholders); `audit_log`. Button disabled after first press; geolocation-denied shows a blocking explanation (arrival without location is not sent — the BRD requires all three values) |
| Capture media on E3 | per bucket | For each item passing E4: Blob upload → (`document` + `next3_outbox 'upload_document'`) in one transaction, folder *Expert documents*, `doc_type` placeholder, `clientRef` = document id |
| Damage diagram | tap-to-mark on static SVG → PNG via canvas | Same pipeline; `doc_type` = diagram placeholder; lands in *Expert documents* (the BRD names that folder even though it is not one of the 4 buckets — recorded, not resolved) |
| Voice note | record → playback confirm | Same pipeline; audio acceptance and doc type are #10 — until answered, the fake accepts audio and the real push is expected to as well |
| Search (E1) | visa or plate | **Local — no NEXT3 call (corrected 2026-08-21, slice 3.2; was `INext3Client.SearchClaims` + cache upsert).** An optional `q` on `GET /api/expert/assignments` (trimmed, max 64) matched against `VisaNo` and the cached `PlateNo` **within the caller's own assignments**, composed into the same statement as the list. Two reasons the original could not ship: a NEXT3-wide search returns claims the expert was never assigned, and the screen it feeds carries a capture panel — attaching photos to a stranger's visa is the failure this project exists to remove; and `ClaimSummary` (§6.2) has no policy number, insured phone or city, so a hit cannot be upserted into `claim` without inventing three fields NEXT3 owns. Consequence recorded: a **cold cache has no plate to match**, so such an assignment is findable by visa only, and E1 says so on screen. `SearchClaims` stays on the port for the officer's lookup (§5.2) |
| Report upload (E5) | file pick (upload allowed — a report is a document, not a car photo) | Same pipeline |

Arrived is **not** enforced as a precondition for capture — the diagram implies an order but the BRD never states the gate, and a roadside expert whose GPS is slow must not be blocked from photographing. Smaller interpretation, recorded.

There is no expert-side "done" state (§1). E1 lists assignments newest-first with media counts; that is the whole lifecycle UI.

### 5.2 Garage + Claim Officer — one declaration state machine, two views

This is **one entity** (`declaration`) with a garage view (G1–G4) and an officer view (O1–O2). The notifications between the two views are part of the module, not an afterthought — they are what people forget to budget.

```
 Draft ──submit──► Submitted ──approve──► Approved ──start repairs──► RepairsInProgress ──submit docs──► RepairDocsSubmitted (terminal)
                      │
                      └──reject──► Rejected (terminal — "Disregard Case")
```

| Transition | Actor | Writes and side effects (in order) |
|---|---|---|
| create Draft | Garage (G2) | `declaration` row (plate required; insured name/note optional — §1 interpretation); documents attach via the §7 pipeline with `push_status` deferred (nothing goes to NEXT3 before approval) |
| **submit** | Garage | state → `submitted`, `submitted_at`; `notification` + push/email to officers (no per-officer assignment — any officer may pick it up; the BRD defines no queueing and we do not invent it); `audit_log` |
| officer review | Officer (O2) | No state change. Officer sees documents, runs visa search (`INext3Client.SearchClaims`). **Visa-not-found branch:** officer creates the visa *directly in NEXT3, outside the app*, and retries the search (BRD NB; confirm #16 — no create-visa API is built) |
| **approve** | Officer | One transaction: state → `approved`, `visa_no` set, `officer_user_id`, `decided_at`, `declaration_comment` rows; then in order — (1) approval + comments rendered to PNG (client-side canvas, idempotent, re-renderable), (2) **outbox rows**: approval PNG + each declaration document as `upload_document` into folder *Survey* (`push_approval` operation for the PNG), (3) `notification` + push to garage, (4) `audit_log`. The PNG render happens before the transaction commits the outbox rows; a render failure aborts the approval cleanly |
| **reject** | Officer | state → `rejected`, comments stored, push to garage, `audit_log`. **Terminal.** No NEXT3 push (diagram 02 pushes only on approval), no resubmit edge — the garage files a new declaration. Rejection comments are stored but the garage view shows only the rejected status, matching the BRD's grant of comment visibility "in case of confirmation" only; flag for the scope letter since it will surprise users |
| start repairs | Garage (G3) | state → `repairs_in_progress` (the "Start Repairs" step exists only in diagram 02 — kept, it is free) |
| submit repair docs | Garage (G4) | Repair photos (capture-only) + discharge/invoice (upload allowed) via the §7 pipeline → outbox `upload_document`, folder *Survey*; state → `repair_docs_submitted` (**terminal** — no closure state, §1); no officer notification (the BRD specifies none; recorded as a gap, not silently added) |

On approval the garage view of G3 unlocks the full claim detail (visa, policy, plate, insured name + phone, make/model, **accident date**) plus officer comments — the declaration is deliberately detail-less until then.

### 5.3 Broker (Options 1 and 2)

**Screens:** B1 list, B2 Option 1 form, B3 create link, B4 review + Send Email, P1 public page. **The broker module never touches NEXT3** — its terminal act is an email routed by insurance type.

Option 1 states: `draft → submitted` (email sent on submit).
Option 2 states: `link_issued → customer_in_progress → ready_to_send → sent` (+ `expired` if the token lapses before submission).

| Step | Writes |
|---|---|
| B2 submit (Option 1) | `broker_request` (6 fields; insurance type from placeholder list #14), documents via §7 pipeline (upload **or** capture, `origin` flag on every row; global upload kill-switch `Broker.AllowUpload` in config), state → `submitted`, `IEmailSender` to the recipient resolved from the routing placeholder (#13), `emailed_at`, `notification`, `audit_log` |
| B3 create link (Option 2) | `broker_request` (option 2, `customer_mobile`), `public_link_token` (raw token shown once to the broker; hash stored), state → `link_issued`. Delivery: **broker copies the link** (channel-agnostic placeholder until #24a; if SMS, `ISmsSender` adds a send here) |
| P1 first open | state → `customer_in_progress`; uniform 404 on invalid/expired/locked token (§9) |
| P1 submit | Validation: 6 fields complete (customer enters Estimated Premium — §1), required documents present, **all 5 car shots present** (front/rear/left/right/roof, capture-only, side selected at capture using the same SVG car as the expert's diagram, each through the clarity gate — car photos are mandatory, the BRD's hard rule). One transaction: field values onto `broker_request`, state → `ready_to_send`, `public_link_token.locked_at` set (token now dead), `notification` + push to the broker, `audit_log` (actor null = public) |
| B4 review → **Send Email** | Broker reviews read-only (the BRD grants review, not edit — smaller interpretation; a broker wanting changes reissues a link) → `IEmailSender` with all information, state → `sent`, `emailed_at`, `audit_log` |

### 5.4 Admin

Deliberately minimal — CRUD + invites + the failed-push screen, nothing more (zero BRD definition beyond "created by application admin").

- **A1 profile management:** the four CRUDs with the §4 field sets; create issues an `invite` row + SMS link (S1 completes registration + OTP verify); deactivate sets `status=inactive` + `inactivated_at` and blocks login — in-flight work is untouched (no BRD guidance; smaller interpretation).
- **A2 failed-push queue:** lists `next3_outbox` where `status='failed'` (and long-`pending`), showing operation, claim/visa, attempts, `last_error`, timestamps; **Retry** resets to `pending` with `next_retry_at = now`. ~4 hours of work; it is the safety net, the debugging tool, the support answer, and the feature AXA will value more than half the BRD.
- Admin sign-in is the same phone-OTP path; the first admin user is seeded by deployment config.

---

## 6. NEXT3 integration

### 6.1 The operations

**The operations, and the written contract for them.** This table used to say "eight operations — seven calls plus one environment obligation", counting `client-doc-src.html` §3.1 as it stood before the 2026-08-19 manager review. **Corrected 2026-08-21, slice 3.3:** the client document (v0.5) had since grown two more rows that this table never absorbed — `GET /experts/{expertId}/claims` and master data for **garages and claim officers**, not experts alone — and the two documents are sent to the same reader. They are folded in below, and `docs/next3-openapi.yaml` (written this slice, the artifact HANDOFF §8 item 3 points at) is the machine-readable version of exactly this set. Paths are illustrative; the operation and the data are the contract.

| Operation | Illustrative path | Direction | Data |
|---|---|---|---|
| Authentication | `POST /auth/token` | app → NEXT3 | Service identity; OAuth client credentials, API key, or mTLS — NEXT3's choice (#1) |
| Claim details | `GET /claims/{visaNumber}` | app → NEXT3 | visa, policy, plate, insured name + phone, make/model, city, accident date. **404 on unknown visa**, never 200-with-empty: §4's cache renders "no such claim" and "NEXT3 is down" as different screens, and the real client maps the 404 to `null` to keep them apart |
| Claim search | `GET /claims/search` | app → NEXT3 | by plate or visa; serves the **officer's** visa lookup (§5.2). Not the expert's E1 search, which is local to their own assignments and makes no NEXT3 call — see §5.1's search row (corrected slice 3.2). Neither term supplied returns empty, never every claim |
| Record arrival | `POST /claims/{visaNumber}/arrival` | app → NEXT3 | date, time, GPS (formats #6) + `clientRef`. The body carries the **instant, the split date and time, and the IANA zone that split used** — so an answer to #6 of "UTC actually" is a config change rather than a re-push of rows whose offset is already gone (§4's `ArrivalInfo`, slice 2.4) |
| Upload document | `POST /claims/{visaNumber}/documents` | app → NEXT3 | **The core of the whole application.** File + document type + folder (*Expert documents* \| *Survey*) + `clientRef` (idempotency — #32) |
| **Expert's assigned claims** | `GET /experts/{expertId}/claims` | app → NEXT3 | **Added to this table slice 3.3**, from client doc §3.1. Two jobs: the expert↔claim linkage authorization depends on (#42/Q5 — it lives in NEXT3 and cannot be reconstructed here), and the **poll fallback** for assignment delivery when NEXT3 cannot call out (#34). Specified, not built — see §6.2 |
| Master data | `GET /experts`, **`GET /garages`, `GET /claim-officers`** | app → NEXT3 | id, name, mobile, active status. **All three, not experts alone** (#8, manager review 2026-08-19): every profile in §2 carries a NEXT3 identity. Only `GET /experts` is on `INext3Client` today — the other two are specified for when onboarding needs them |
| Assignment notification | inbound call from NEXT3 | **NEXT3 → app** | visa assigned to expert; HMAC-signed, replay-safe on `assignmentRef`; poll fallback above if NEXT3 cannot call out (#34) |
| Sandbox | — | environment | Non-production, representative test data (#1). Not an API call — saying so beats silently inventing an endpoint |

### 6.2 The interface and the fake

```csharp
public interface INext3Client
{
    Task<ClaimDetail?> GetClaim(string visaNo, CancellationToken ct);
    Task<IReadOnlyList<ClaimSummary>> SearchClaims(string? plateNo, string? visaNo, CancellationToken ct);
    Task RecordArrival(string visaNo, ArrivalInfo info, string clientRef, CancellationToken ct);
    Task UploadDocument(string visaNo, DocumentPush doc, string clientRef, CancellationToken ct);
    Task<IReadOnlyList<Next3Expert>> GetExperts(CancellationToken ct);
}
```

**Fake contract** (the fake is a deliverable, not a stub): implements the full interface against seeded in-memory/SQL data; configurable failure injection (`Fake:FailureRate`, `Fake:LatencyMs`) so retry, backoff, and the failed-push screen are demonstrable without NEXT3; selectable per environment by config (`Next3:Mode = fake | real`) **all the way to handover** — UAT can run on it if the sandbox slips, and that fact goes in the status report, not under the rug. Nothing outside `INext3Client`/`IAssignmentSource` may know which implementation is live; no feature code calls NEXT3 directly.

**Assignment delivery (#34):** one abstraction, `IAssignmentSource`, emitting `AssignmentReceived(visaNo, expertNext3Id, next3AssignmentRef)` into a single idempotent handler (dedupe on `next3_assignment_ref`; a replayed webhook or overlapping poll is a no-op). Three sources: **webhook receiver** (preferred — HMAC-signed with a shared secret, replay-windowed), **poller** (worker polls on an interval), **fake** (injects synthetic assignments for demo/UAT). #34's answer flips a config value and touches one adapter; nothing downstream changes.

### 6.3 The outbox

All NEXT3 writes flow through `next3_outbox` (schema in §4, verbatim from HANDOFF §3). Never synchronous: users are at accident scenes on bad connections and NEXT3 is a legacy core with outages and maintenance windows — sync means NEXT3 down = experts cannot work; async means the queue drains on recovery and nobody notices.

Worker loop (Container Apps Job, every ~30s):

```sql
CREATE OR ALTER PROCEDURE dbo.next3_outbox_dequeue
    @batch_size int, @now datetime2 = NULL,
    @lease_seconds int = 300, @max_attempts int = 8
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @t datetime2 = COALESCE(@now, SYSUTCDATETIME());

    -- Retire a row that keeps outliving its worker: the give-up rule lives in code that only
    -- runs when a worker survives to record an outcome, so without this the lease would
    -- re-push such a row for ever and it would never reach A2.
    UPDATE dbo.next3_outbox WITH (UPDLOCK, READPAST, ROWLOCK)
    SET status = 'failed',
        last_error = COALESCE(last_error, 'Abandoned: lease expired with no outcome recorded.')
    WHERE status = 'processing' AND next_retry_at <= @t AND attempts >= @max_attempts;

    UPDATE TOP (@batch_size) dbo.next3_outbox WITH (UPDLOCK, READPAST, ROWLOCK)
    SET status = 'processing', attempts = attempts + 1,
        next_retry_at = DATEADD(second, @lease_seconds, @t)
    OUTPUT inserted.*
    WHERE next_retry_at <= @t AND status IN ('pending', 'processing');
END
```

**The lease needs an arbitrator, and `attempts` is it.** A reclaimed row is held by two workers — the new one and the original, which may still be alive and about to record its outcome. Last-writer-wins would let a stale worker rewrite a `sent` row to `failed`, or a `failed` row to `sent` with a timestamp it never earned; §7.3 deletes blobs on `sent` and A2 lists `failed`, so both the retention rule and the safety net would act on a status NEXT3 never agreed to. `attempts` is therefore the **concurrency token**: a reclaim increments it, so every outcome write carries `AND attempts = <the value this worker claimed>` and the stale one finds no row. No new column — the claim's attempt number *is* its generation counter — so §4's column list is untouched.

**Ordering is deliberately absent.** Draining oldest-first was tried as a CTE with `ORDER BY created_at`; the sort materialises the candidate set and takes locks on exactly the rows `READPAST` exists to let a second worker step over, turning the non-blocking claim into a blocking one. Non-blocking concurrent drain wins. Both hint sets are proven load-bearing by tests that go red when the hint or the token is removed.

(`READPAST` is SQL Server's `SKIP LOCKED`; this dequeue is one of the three sanctioned stored procedures.) Success → `sent` + `sent_at`. Failure → `last_error`, backoff **1 min → 5 min → 30 min → 2 h → 6 h …**, after ~8 attempts → `failed` (surfaced on A2 with Retry). Backoff ceiling is a config knob to be re-tuned when #33 (NEXT3 maintenance windows) is answered. The full schedule takes **26 h 36 m** to exhaust — that is how long a doomed push takes to reach A2, and it is pinned by a test so a retune is a decision rather than a side effect.

**Two parameters beyond the original sketch (realized 2026-08-20, slice 2.2).** `@now` carries the *worker's* clock rather than the database's: the app writes `next_retry_at` from `TimeProvider`, so a procedure comparing against `SYSUTCDATETIME()` would measure app time against wall-clock time and make the whole retry schedule untestable. `@lease_seconds` pushes `next_retry_at` out while a row is claimed, so a worker that dies between claiming a row and recording its outcome has that row reclaimed instead of stranded in `processing` for ever — never retried, and absent from A2's `failed` list. Re-pushing a row whose worker was merely slow is safe precisely because every push carries a stable `clientRef`.

**Transient vs permanent (realized 2026-08-20, slice 2.2).** Only transient failures consume the retry schedule: `FakeTransientException`, `HttpRequestException`, `TimeoutException`. Anything else goes to `failed` on the first attempt — a push against a visa NEXT3 does not know, a malformed payload, or a bug will never be fixed by waiting, and A2's Retry makes the decision reversible. Each message commits its own transition, so one poison row cannot roll back the outcomes of the rows beside it.

**The two classic bugs, designed out:**
- **Idempotency:** every push carries `clientRef` = the document id (stable across retries). Whether NEXT3 dedupes on it is #32, and the OpenAPI proposal (`docs/next3-openapi.yaml`, written slice 3.3) makes `clientRef` a **required** parameter and states the dedupe as a requirement on NEXT3, so the obligation is visibly theirs. **Corrected 2026-08-21, slice 3.3: there is no client-side sent-log** — this row used to promise one "until #32 is answered", and building it would have been wrong three times over. The outbox row's `sent` status already *is* the sent-log, so a second store of the same fact is a second answer that can disagree with it (3.1's normalise-once lesson). It cannot cover the case it exists for: the dangerous retry is the one after a **timeout**, where nothing client-side knows whether NEXT3 accepted the push — marking it sent loses a document, marking it unsent duplicates one, and only the receiver can tell which. And architecture rule 4 forbids `Api.Integrations.Next3` from referencing an outbox row at all, so it could not have been built where it was described.
- **Blob deletion:** no blob is deleted before its outbox row is `sent` (§7). This rule is restated here because it is reliably broken during a week-6 "cleanup" refactor.

If #5 resolves to "shared directory + DB inserts" instead of an API, `RealNext3Client` becomes a directory-writer + DB-inserter (second sanctioned stored-proc site) behind the **same interface** — the outbox, worker, and all feature code are unchanged. That is the point of the boundary.

---

## 7. Media pipeline

### 7.1 Capture-only enforcement

| Context | Bucket | Upload | Capture |
|---|---|---|---|
| Expert | Insured Documents | yes | yes |
| Expert | Insured Car Photo | **no** | yes |
| Expert | TP Documents | yes | yes |
| Expert | TP Car Photo | **no** | yes |
| Expert | Report / voice / diagram | yes (report) / n-a | n-a / yes |
| Expert | Voice note (`voice_note`) — realized slice 3.1 | **no** | recorded in-app |
| Expert | Damage diagram (`damage_diagram`) — realized slice 3.1 | **no** | drawn in-app |
| Garage | Documents (survey, discharge, invoice) | yes | yes |
| Garage | Car photos (declaration + repair) | **no** | yes |
| Broker Option 1 | Photos + documents | yes (kill-switch `Broker.AllowUpload`) | yes |
| Public customer (Option 2) | Supporting documents (ID card, car papers) | yes | yes |
| Public customer (Option 2) | Car photos — 5 mandatory slots (front/rear/left/right/roof, side selected at capture) | **no** | yes |

Enforcement: in the Capacitor shell, capture-only buckets call the native camera directly (no picker) — **provisional pending `research-capacitor.md`** confirming the plugin can suppress gallery access on both platforms. In the browser, `capture="environment"` on the input is a hint, not a guarantee; the `origin` flag on every `document` row records provenance regardless, and the server rejects files whose metadata shows a non-capture origin for capture-only buckets on a best-effort basis. The week-6 device checkpoint validates the native path; if it fails, this section is where Flutter gets reconsidered.

### 7.2 Clarity gate (the BRD's "image visibility and voice clarity must be ensured")

Client-side, before upload — **not ML** (#9):
1. Resolution floor: min dimensions (`Clarity.MinWidth/MinHeight` placeholders), measured at the image's **native** size.
2. Blur check: grayscale → Laplacian convolution → variance; below `Clarity.BlurVarianceThreshold` → retry prompt with re-capture. **Run at a fixed analysis scale, `Clarity.BlurAnalysisMaxEdge` (realized 2026-08-20, slice 2.5)** — variance scales with resolution, so without a fixed edge the same photo scores differently on a 12 MP and a 48 MP handset and the threshold means a different thing on every device. The pair is meaningless apart, so the scale is a placeholder key beside the threshold rather than a constant in the web code. The decoder therefore returns native dimensions plus a *bounded* pixel buffer: one RGBA array for a 48 MP photo is ~192 MB, which kills the tab on the handset of an expert standing at a crash site.
3. User confirm screen (E4) showing the image full-bleed with Retake / Confirm.
4. Voice notes: playback-confirm only (§1).
5. Server re-validates dimensions, size, and content type on every upload (the client check is UX, not security — mandatory on the public surface).

Applies at every media entry point, uploads included, per the BRD's "all profile steps" — with one recorded exception: **PDFs skip items 1–2**, having neither dimensions nor blur, and are stored `clarity_result = not_applicable`. A file-picked *image* is still gated; "uploads included" is about provenance, not file type.

**The two slice-3.1 artifacts skip the blur pass for the same reason, from opposite directions** (realized 2026-08-20). A **voice note** is item 4's playback-confirm — no dimensions, no focus, `clarity_result = not_applicable`; the server verifies only that the bytes are the container they were declared to be (containers, not codecs: `MediaRecorder` emits `audio/webm;codecs=opus` on Chrome and `audio/mp4` on Safari, so the declared type is normalised to its media type before the allow-list compare and the *validated* value is the one stored, on the row, the blob and the NEXT3 payload alike). A **damage diagram** is a canvas export: it is an image and item 1's floor applies to it server-side exactly as to a photograph — which is why the web module renders at a fixed size above the floor — but item 2 does not, because a vector drawing has no focus to measure and scoring one against `BlurVarianceThreshold` would tell an expert to hold the phone steadier. The client hook therefore takes a `photographic` flag, defaulted true, rather than deciding from the MIME type alone.

**The thresholds reach the browser over `GET /api/config/media`** (anonymous, realized 2026-08-20, slice 2.5), which also serves `Media.MaxFileMb` and §7.1's bucket rows. A client-side gate needs client-side values, and CLAUDE.md's placeholder rule names thresholds explicitly — hardcoding them in TypeScript would be both a placeholder violation and a second source of truth that drifts from the values the server rejects uploads with. Anonymous because §5.3's public page runs the same gate with no token; it is outside `/public/*`, so §9.1's rate limits do not cover it, which is acceptable only because it touches no database and no user.

### 7.3 Blob lifecycle

```
captured/selected → clarity pass → confirmed
   → [one transaction: document row + outbox row]  +  blob PUT (transit container)
   → outbox worker pushes to NEXT3 → status 'sent'
   → cleanup job deletes blob after (sent_at + Retention.BlobDays)
```

Rules:
- **No blob is deleted before its outbox row is `sent`.** The cleanup job's query joins on `next3_outbox.status = 'sent'` — structurally, not by convention. Realized 2026-08-20, slice 2.3: the join is `document.outbox_message_id → next3_outbox.id`, composed as a single SQL statement via `OutboxSentQuery`, which hands the media module an `IQueryable<Guid>` of confirmed pushes so no type outside `Api.Outbox` ever touches an outbox row (architecture rule 4 stays intact). A row that is `pending`, `processing` or `failed` cannot enter the eligibility set at all.
- `failed` rows retain their blobs indefinitely (they are what A2's Retry re-sends).
- The sweep commits **one document per transaction**, and wraps each blob delete on its own. A batch-wide commit was written first and was actively dangerous: one undeletable blob threw out of the loop, discarding the `blob_deleted_at` and audit rows already staged for bytes that were physically gone — and because the batch is deterministic, the same poison row then stalled retention behind it for ever. Same lesson as §6.3's one-`SaveChanges`-per-message.
- **Orphan sweep** (§7.3's "a blob without a row is garbage the cleanup job sweeps"): anything in the container older than `Retention.OrphanBlobHours` that no live `document` row claims. This is the backstop for the accepted failure below — and for a retention delete whose flag write failed after the bytes went.
- Broker media (`push_status = n/a`) has no outbox row, so it is **structurally excluded** from the sweep and simply survives; `Retention.BrokerBlobDays` is not yet enforced and belongs to slice 5.2/5.3, which owns `broker_request`'s terminal states. Retaining too long is the safe side of this rule.
- Broker-module media (`push_status = n/a`) never goes to NEXT3; its blobs are retained until `Retention.BrokerBlobDays` after `sent`/terminal state — the email carries the information; long-term custody of a member of the public's identity documents is not this app's job (§9, and #4/#22 for the client's word on retention).
- Blob PUT happens outside the DB transaction (a blob without a row is garbage the cleanup job sweeps; a row without a blob is an error surfaced at push time — the safe failure order).
- Steady state ~11 GB, flat forever; NEXT3 is the system of record (#4 confirms).

---

## 8. Notifications

All sends go through `IEmailSender` / `IPushSender` / `ISmsSender`, each with a fake, each logged to `notification`.

| Event | Channel | Recipient | Notes |
|---|---|---|---|
| Claim assigned to expert | Push (popup) | Expert | The BRD's primary trigger. **Web push is built (slice 3.4)**: `Push:Mode = fake \| webpush` selects `WebPushSender`, which sends to every live `push_subscription` of the user, revokes on 404/410, and writes **one `notification` row per subscription attempted** — plus one when the user has none, because "nobody was told" is the case `AssignmentHandler` must hear about in order to leave `notified_at` null. It throws unless at least one device accepted, so partial success is success. Payload `{title, body, url}` with `url = /expert/{assignmentId}`, read by `src/Web/public/sw.js`. Native APNs/FCM via Capacitor stays slice 6.3 and provisional |
| Declaration submitted | Push (+ email fallback) | Claim officers | No per-officer routing — all officers |
| Declaration approved / rejected | Push (popup) | Garage | Approval unlocks detail view; rejection shows status only |
| Option 2 file ready to send | Push | Broker | On customer submission |
| Broker request submitted (both options) | **Email** | AXA recipient by insurance type | Routing table `Broker.EmailRouting` is a placeholder — the real recipients are #13, the type list #14. **Never invented** |
| Profile invite | SMS | New user | Invite link (S1) |
| Login | SMS | User | OTP (#7/#40 decide the gateway; no cost quoted until then) |
| Option 2 customer link | SMS **if** #24a says SMS | Customer | Until answered: broker copies the link; adding the SMS send is one line at B3 |

Push per platform (APNs via Capacitor, FCM, web push incl. aggressive-battery-saver OEM behavior common in MENA) is exactly what `research-capacitor.md` must validate before week 1 — flagged provisional.

---

## 9. Security and data protection

**Authentication.** Phone + OTP for all five authenticated roles: `otp_challenge` rows hashed, TTL `Auth.OtpTtlMinutes`, max `Auth.OtpMaxAttempts`, resend throttled. Invite flow: SMS link → S1 → OTP verify → account active. Sessions: short-lived JWT (role claim) + refresh token; tokens invalidated on deactivation.

**Authorization.** Role per user (§4 `app_user.role`), enforced server-side with ASP.NET authorization policies per endpoint group matching the §2 matrix exactly. Resource-level checks on top: a garage sees only its declarations, a broker only its requests. The `/public/*` group has its own policy that authenticates a *token*, never a user.

**Audit trail.** The BRD omits one; claims disputes make it mandatory. `audit_log` (append-only) records: login success/failure, every media upload (who, which claim/declaration/request, when, origin flag), Arrived presses with coordinates, every declaration transition with actor, approval/rejection with comments hash, broker email sends with recipient, admin CRUD, outbox retries from A2, public-page submissions (actor null, token id logged). This is the InfoSec answer to "who uploaded which photo, when".

**Data protection.** PII at rest: Azure SQL TDE + Blob SSE (platform defaults) — data residency (which Azure region) is #22 and is AXA's call; the region is deployment config, nothing in the design binds to one. TLS everywhere. Blob access via short-lived SAS only; no public containers. Retention: transit-buffer rules of §7.3; the app is deliberately not a long-term PII store.

### 9.1 Broker Option 2 — the public surface

The project's largest attack surface: a public, unauthenticated page collecting identity documents from members of the public. This subsection is written to survive an AXA Group InfoSec reading (#21 — review/pen test is likely and is on the critical path if required).

**Token.** 256-bit random from a CSPRNG, base64url in the URL (`/p/{token}`); only the SHA-256 hash stored (`public_link_token.token_hash`) — a DB leak does not leak live links. **Reusable until first successful submission, then locked** (`locked_at`): the customer may leave and return to an unfinished form, but each link accepts exactly one submission. **Validity `PublicLink.ValidityDays` = 7 (placeholder)**. Invalid, expired, and locked tokens all return the **same uniform 404** — no distinguishable states to enumerate. Brokers can reissue (which creates a new request + token); reissue does not resurrect a locked one.

**What the page exposes before submission: nothing.** The 6 empty fields, the broker's display name, the capture UI. No policy data, no PII, no pre-fill, no NEXT3 touch (the boundary in §3 is structural). Estimated Premium is customer-entered (§1 decision; #24c flagged).

**Abuse controls.** Per-IP and per-token rate limits on every `/public/*` endpoint (ASP.NET rate limiting middleware, thresholds in `PublicLink.RateLimit.PerIpPermitsPerMinute` / `PerTokenPermitsPerMinute` — the two are **chained**, so varying either the address or the token does not buy fresh budget, and the per-token key is the token's *hash*, so an invalid token throttles identically to a real one and the limiter cannot be used to probe which links exist; realized 2026-08-19, slice 1.5); hard caps on file count and size per submission (`PublicLink.MaxFiles`, `PublicLink.MaxFileMb`); content-type allow-list re-validated server-side; clarity check re-run server-side for dimensions/size (client JS is not a control); submissions throttled per token; no error detail leaks. CAPTCHA deliberately omitted (link possession is the gate; a 256-bit token is not guessable) — revisit only if #21's review demands it.

**All four #24 parameters** (delivery channel, validity, reuse, premium ownership) are blocked on the client; every one resolves to config or a form-level change, none to architecture. **#38 (WAF ~$330/month — more than the entire rest of the application)** is a cost decision AXA must make for this surface; it layers in front of, and does not replace, the controls above.

---

## 10. Environments, CI/CD, migrations

Nothing on this exists in any prior document — this section is net-new and deliberately minimal.

**Environments: two** — recommendation on record (#41; AXA's call, roughly doubles the Azure figure, though test's scale-to-zero keeps the true delta to ~$20–40/month):

| | test | production |
|---|---|---|
| Container Apps | scale-to-zero | `minReplicas: 1` |
| SQL | cheapest tier / serverless | S1 or serverless GP |
| NEXT3 | **fake by default**; sandbox when it exists | real |
| Senders | fakes (email/SMS to log) | real |
| Purpose | demos, UAT, fixes approved without touching live claims | live |

**CI/CD (GitHub Actions, trunk-based on `main`):** every merge → build + tests → container image → ACR → **deploy to test** automatically. **Production promotion = pushing a git tag, which redeploys the *same image digest*** — no rebuild between environments. Rollback = re-point to the previous digest. Capacitor builds: Android on the GitHub runner; iOS on Codemagic's personal-tier free macOS minutes (per HANDOFF §4 — confirm the build actually runs there before week 6; store accounts AXA's, CI account the developer's).

**EF Core migrations:** generated migration bundle executed as a pipeline step **before** the new revision goes live; production never auto-migrates on startup. Test may migrate on startup for speed. Never hand-edit a generated migration's applied history.

**Secrets:** Container Apps secrets/env vars (single tenant; no Key Vault ceremony unless #21 demands it). All accounts in AXA's name on AXA's card from day one.

---

## 11. Revised week-by-week plan

Supersedes the 8-week table in `estimate-and-plan.md` (which predates Broker Option 2). **Decision 2026-08-19: the calendar holds at 8 weeks.** Option 2's ~1.5–2.5 weeks is paid for by (a) reusing the expert capture/clarity/upload pipeline for the public flow, (b) dropping damage-diagram polish, and (c) **dropping the second UAT round**.

| Wk | Work (changes vs the old plan in bold) |
|---|---|
| 1 | Repo, phone-OTP auth, RBAC, admin CRUD ×4 + invites, fake NEXT3 client behind `INext3Client`/`IAssignmentSource`, **Option 2 schema (`public_link_token`, `broker_request`) + public-surface API skeleton** (cheap now, expensive to retrofit) |
| 2 | Expert core: claim list/detail, Arrived + geolocation, capture component + clarity gate, 4 buckets, upload pipeline + outbox write path |
| 3 | Expert rest: voice note, car diagram (**functional, no polish**), search, report upload. Swap fake → real NEXT3 — **gated on the sandbox (#1); if late, stay on the fake and say so in the weekly status, the date does not silently absorb it** |
| 4 | Garage + Officer state machine (both views), web push, **CLIENT DEMO → 30% payment** (calendar position protected) |
| 5 | Repair uploads, rejection paths, Broker Option 1 + email routing, **Option 2 public form (6 fields + document upload)** |
| 6 | **Option 2 capture flow (5 shots + side selection + clarity — reusing the §7 pipeline) + broker ready-to-send queue + Send Email.** Hardening: outbox retry, failed-push admin screen. Capacitor wrapper + **real-device checkpoint** (3–4 handsets; push + camera validated; Flutter reconsidered here if they fail — calendar position protected for the #29/#30 lead times) |
| 7 | **Security pass on the public surface (rate limits, token paths, upload caps) + Option 2 spillover buffer.** Audit log completion, file-size limits, error states, UAT prep |
| 8 | **UAT — single round + capped fix window**, deploy, handover docs (runbook, account transfer), training session |

**The trade-off, stated plainly:** cutting UAT round 2 spends the contractual defence against UAT scope creep — previously "the defence" for the weeks-7–8 risk window. Consequences the scope letter must now absorb: UAT round 1 gets a tight written definition (participants, acceptance criteria, capped defect-fix window — ties to #25), and anything beyond it is a separate quote. **There is no remaining descope lever.** Offline mode was already out, diagram polish and UAT2 are now spent; any further slip — a late sandbox (#1), an InfoSec review on our clock (#21), a NEXT3 vendor delay (#31) — moves the date day-for-day, and that is said to the client now, not discovered in week 7. The week-4 payment milestone and week-6 device checkpoint stay where they are in all scenarios.

---

## 12. Design decisions blocked on client answers

Placeholder strategy: every unresolved value is a named key in **one config file** (Appendix A), with obviously fake values, never invented client data. Each answer below changes config or one adapter — none changes architecture; that is what the interfaces are for.

| # | Blocked decision | Placeholder / interim design | What changes when answered |
|---|---|---|---|
| 1 | Real NEXT3 client, auth method, rate limits; the week-3 fake→real swap | **`RealNext3Client` is built and unit-tested against a stubbed transport (slice 3.3), and `Next3:Mode` is still `fake` everywhere** — the swap did not happen in week 3 because the gate was closed. `docs/next3-openapi.yaml` is written and is the file to send. Both auth modes (`apikey`, `oauth`) are implemented; mTLS is config-ready and unexercised | A sandbox URL plus credentials in config, and `Next3:Mode=real`. The contract suite (`Next3ClientContractTests`) already has its sandbox half written and skipping, so the swap is a config value and a test run. Late answer = fake through UAT, said out loud in the weekly status |
| 2 | Network path to NEXT3 (internet / VPN / allowlist / tunnel) | Design assumes HTTPS reachable from Container Apps; outbound tunnel proposed first if internal-only | Deployment config; possibly a tunnel component on AXA's side |
| 5 | API vs shared-directory + DB insert | API-shaped interface; outbox unchanged either way | `RealNext3Client` internals only (directory writer + sanctioned stored proc) |
| 6 | Arrived field names/formats — **and which clock the date and time are in** | `Next3.Arrival*` placeholder mapping, incl. `ArrivalTimeZone`. The outbox payload deliberately carries the **instant**, not a date-and-time pair: an expert arriving at 01:30 GST would otherwise be queued as arriving the previous day, and a queued row cannot be repaired from a value already collapsed into the wrong zone (realized 2026-08-20, slice 2.4) | Field mapping **and the zone split** in the real client |
| 7 / 40 | SMS gateway + provider | `ISmsSender` fake logs sends; no cost quoted | Real sender adapter + AXA account. Note: Option 2 adds a second send stream on top of OTP if #24a says SMS |
| 10 / 12 | Audio acceptance; document-type codes | `Next3.DocTypes.*` placeholders incl. voice + diagram | Config values; if audio refused, voice notes need a client conversation (feature is in the BRD) |
| 13 / 14 / 15 | Email routing table, insurance-type list, IRIS codes | `Broker.InsuranceTypes` (2 BRD examples + obvious fakes), `Broker.EmailRouting` (fake recipients), IRIS free-text | Config values only |
| 16 | Create-visa location | No create-visa API built; officer works in NEXT3 directly | If AXA wants in-app creation: new operation + scope conversation (it is not in scope) |
| 21 | InfoSec review / pen test | §9 written to survive review; Key Vault, CAPTCHA, WAF all deferred | Whatever the review demands — **on AXA's clock unless agreed otherwise; likeliest thing to blow past 2 months** |
| 23 | Delivery model confirmation | Superseded by the Capacitor decision (§1); confirm the wrapper + browser split | Nothing if confirmed; Flutter conversation only if the week-6 checkpoint fails |
| 24 | Option 2 link channel, validity, reuse, premium owner | Broker-copies-link; 7 days; reusable-until-locked; customer enters premium | (a) one send at B3; (b)(c) config; (d) form-level move |
| 28–30 | Device mix, store vs MDM, Apple account | Both platforms assumed; Codemagic for iOS | 80%+ Android would shrink iOS risk to near zero; MDM removes review cycles; no Apple account = start provisioning **now** (multi-week) |
| 31 | Who builds NEXT3's endpoints, budgeted/scheduled? | Fake covers ~through week 3 | Decides whether "they'll provide endpoints" means two weeks or two months — schedule risk #1, above InfoSec |
| 32 | NEXT3 `clientRef` dedupe | clientRef sent on both writes always; **no client-side sent-log** (corrected slice 3.3 — see §6.3). `docs/next3-openapi.yaml` makes it a *required* parameter and states the dedupe as a requirement on NEXT3 | If NEXT3 dedupes: retries are safe, which is what the whole outbox assumes. If not: **duplicate documents under a visa are unavoidable on any timed-out retry**, nothing on our side can prevent it, and that goes in the runbook and the scope letter rather than being papered over with a local check that cannot see what NEXT3 did |
| 33 | NEXT3 availability windows | Backoff ceiling 6 h, config knob | Retune backoff; maintenance windows into the runbook |
| 34 | Assignment webhook vs poll | `IAssignmentSource` exists with **the fake built and active**; webhook and poll are designed and throw loudly at startup if selected (corrected slice 3.3 — this row said "all three sources built", which was never true). Both are now *specified*: the callback and `GET /experts/{expertId}/claims` are in `docs/next3-openapi.yaml` | One adapter plus a config flip. Nothing downstream changes — the single idempotent handler and its `next3_assignment_ref` dedupe are already built (slice 2.1) |
| 35 / 36 | Mandated DB platform; NEXT3's engine | Azure SQL assumed (their shop) | #35 contrary answer = real rework, raise immediately. #36 only matters if #5 says directory+DB |
| 37 | Azure resource-group access | Dev subscription until granted | Deploy target switch; **request in week 1 — often slower than API credentials** |
| 38 | WAF requirement | §9.1 controls stand alone; no WAF budgeted | Front Door Premium ~$330/mo on AXA's bill if required |
| 41 | One environment or two | Designed for two (§10); costs assume it | One env = delete the test column and the demo-without-live-data story |
| 4 / 22 | Photo custody + data residency | NEXT3 system-of-record, days-only blobs, region as config | Contrary #4 answer (app as archive) = storage grows ~68 GB/month and the §7 lifecycle is redesigned — that is a scope conversation |
| 20 | Volumes | 100 claims/day, 15 × 1.5 MB, 200 users | Sizing + SMS/rate-limit numbers; Option 2 customer-link volume is unasked — added to the next client email |

---

## Appendix A — placeholder config (the one file)

All placeholders live in `appsettings.Placeholders.json`, loaded last in configuration order so real values override without code changes. Obviously fake values only. Grep rule: any client-specific literal found outside this file is a bug.

```jsonc
{
  "Next3": {
    "Mode": "fake",                          // fake | real            (#1)
    "BaseUrl": "https://PLACEHOLDER-next3.example",
    "AuthMode": "PLACEHOLDER",               // apikey | oauth         (#1)
    // The rest of the auth block realized 2026-08-21, slice 3.3. Validated only when Mode = real:
    // every value here is a placeholder, so an always-on validator would stop the app booting.
    "ApiKey": "PLACEHOLDER-next3-api-key",
    "OAuth": {
      "TokenUrl": "https://PLACEHOLDER-next3.example/auth/token",
      "ClientId": "PLACEHOLDER-client-id",
      "ClientSecret": "PLACEHOLDER-client-secret"
    },
    // mTLS is config-ready and deliberately NOT exercised — nothing exists to test it against (#1).
    "ClientCertificatePath": "PLACEHOLDER-client-certificate-path",
    // A real deadline: a hung NEXT3 must land on §6.3's retry schedule, not hold its outbox lease.
    "TimeoutSeconds": 30,
    "DocTypes": {                            // (#12, #10)
      "InsuredDocument": "PLACEHOLDER-DOC-01",
      "InsuredCarPhoto": "PLACEHOLDER-DOC-02",
      "TpDocument": "PLACEHOLDER-DOC-03",
      "TpCarPhoto": "PLACEHOLDER-DOC-04",
      "ExpertReport": "PLACEHOLDER-DOC-05",
      "VoiceNote": "PLACEHOLDER-DOC-06",
      "DamageDiagram": "PLACEHOLDER-DOC-07",
      "ApprovalImage": "PLACEHOLDER-DOC-08",
      "RepairPhoto": "PLACEHOLDER-DOC-09",
      "Discharge": "PLACEHOLDER-DOC-10",
      "Invoice": "PLACEHOLDER-DOC-11"
    },
    "ArrivalFieldMap": "PLACEHOLDER",        // (#6)
    "ArrivalTimeZone": "PLACEHOLDER-IANA-ZONE", // (#6) which clock §6.1's "date, time" are in
    "AssignmentSource": "fake"               // webhook | poll | fake  (#34)
  },
  "Broker": {
    "AllowUpload": true,                     // Option 1 kill-switch (BRD)
    "InsuranceTypes": [                      // (#14) — first two are the BRD's own examples
      "MOTOR ALL RISK", "MOTOR TOTAL LOSS",
      "PLACEHOLDER-TYPE-3"
    ],
    "EmailRouting": {                        // (#13) — never real addresses
      "MOTOR ALL RISK": "PLACEHOLDER-recipient-1@example.invalid",
      "MOTOR TOTAL LOSS": "PLACEHOLDER-recipient-2@example.invalid",
      "PLACEHOLDER-TYPE-3": "PLACEHOLDER-recipient-3@example.invalid"
    }
  },
  "PublicLink": {                            // (#24)
    "ValidityDays": 7,
    "DeliveryChannel": "copy",               // copy | sms
    "MaxFiles": 15,
    "MaxFileMb": 10,
    "RateLimit": {                           // §9.1 abuse controls (realized 2026-08-19, slice 1.5)
      "PerIpPermitsPerMinute": 60,
      "PerTokenPermitsPerMinute": 20
    }
  },
  "Clarity": {                               // (#9); served to the browser by GET /api/config/media
    "MinWidth": 1024,
    "MinHeight": 768,
    "BlurVarianceThreshold": 100,
    "BlurAnalysisMaxEdge": 512               // the scale that threshold is measured at (slice 2.5)
  },
  "Auth": {
    "OtpTtlMinutes": 5,
    "OtpMaxAttempts": 5,
    "OtpResendSeconds": 60,                  // §9 "resend throttled"
    "InviteValidityDays": 7,
    "Jwt": {
      "Issuer": "PLACEHOLDER-axa-motor-claims",
      "Audience": "PLACEHOLDER-axa-motor-claims",
      "SigningKey": "PLACEHOLDER-dev-only-signing-key-0000000000000000",  // >= 32 bytes; prod overrides via Auth__Jwt__SigningKey env var
      "AccessTokenMinutes": 15,
      "RefreshTokenDays": 14
    },
    "SeedAdmin": {                           // §5.4: first admin seeded by deployment config
      "Phone": "+999000000001",              // +999 = unassigned country code, obviously fake
      "DisplayName": "PLACEHOLDER Admin"
    }
  },
  "Blob": {                                  // §7.3's transit buffer (realized 2026-08-20, slice 2.3)
    "Mode": "fake",                          // fake (in-memory) | azure (Azurite locally)
    "ContainerName": "media-transit"
  },
  "Push": {                                  // §8's push rows (realized 2026-08-21, slice 3.4)
    "Mode": "fake",                          // fake | webpush
    "Vapid": {
      // A VAPID private key is a real credential: whoever holds it can send notifications that
      // browsers accept as coming from AXA. These three stay PLACEHOLDER for ever — real values live
      // in `dotnet user-secrets` locally and Container Apps secrets in deployment (CLAUDE.md has the
      // commands). Generate a pair with `npx --yes web-push generate-vapid-keys`.
      "Subject": "mailto:PLACEHOLDER-push-contact@example.invalid",
      "PublicKey": "PLACEHOLDER-vapid-public-key",
      "PrivateKey": "PLACEHOLDER-vapid-private-key"
    },
    "TimeoutSeconds": 15,
    // Real values, not placeholders — these are the browsers' own push services, not client data,
    // the same treatment as Media:ImageContentTypes. An **SSRF control**: the endpoint a browser
    // registers is a URL this API later POSTs to, so an unchecked one would let any authenticated
    // user aim it at a host reachable only from inside the deployment. Empty disables the check.
    "AllowedEndpointHosts": [
      "fcm.googleapis.com", "android.googleapis.com",
      "updates.push.services.mozilla.com", "notify.windows.com", "push.apple.com"
    ],
    // The sender walks a user's subscriptions serially on the assignment-ingestion path, so an
    // unbounded set is an unbounded stall — for every expert, not just that one.
    "MaxSubscriptionsPerUser": 10
  },
  "Media": {                                 // §7.2 item 5's server-side re-validation
    "MaxFileMb": 15,
    "ImageContentTypes": [ "image/jpeg", "image/png" ],
    "DocumentContentTypes": [ "image/jpeg", "image/png", "application/pdf" ],
    // (#10) realized 2026-08-20, slice 3.1. Also the list the browser's recorder picks its format
    // from, so no audio format literal is ever written into TypeScript.
    "AudioContentTypes": [ "audio/webm", "audio/mp4", "audio/ogg" ]
  },
  "Retention": {                             // (#4); the last four realized 2026-08-20, slice 2.3
    "BlobDays": 7,
    "BrokerBlobDays": 30,                    // not yet enforced — slice 5.2/5.3, see §7.3
    "OrphanBlobHours": 24,
    "OtpChallengeHours": 24,                 // §4's otp_challenge TTL purge, deferred here by 1.2
    "CleanupEnabled": true,                  // false in tests; the sweeps are driven explicitly there
    "PollMinutes": 60
  },
  "Outbox": {                                // (#33); the rest realized 2026-08-20, slice 2.2
    "MaxAttempts": 8,
    "BackoffCeilingHours": 6,
    "BatchSize": 10,                         // §6.3's UPDATE TOP (10)
    "PollSeconds": 30,                       // §6.3's "every ~30s"
    "LeaseSeconds": 300,                     // reclaim window for a worker that died mid-push
    "WorkerEnabled": true                    // false in tests; the loop is driven explicitly there
  },
  "Fake": {                                  // §6.2 failure injection (realized 2026-08-19, slice 1.4)
    "FailureRate": 0.0,                      // [0,1] chance any fake call throws FakeTransientException
    "LatencyMs": 0                           // artificial latency before a fake call completes
  }
}
```

`Fake:*` is not client data — it is the knob that makes the §6.2 fakes demonstrably realistic. It lives here because the placeholder file is loaded with `reloadOnChange`, so the week-4 demo can "kill NEXT3 mid-flow" and show the queue drain on recovery without a restart. Defaults are zero, so nothing is flaky unless asked.
