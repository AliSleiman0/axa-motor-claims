# HANDOFF — read this first

**Project:** AXA Middle East — Mobile Application for Motor Claim Management
**Developer:** solo (Ali Sleiman)
**Commitment:** 2 months, $5,000 fixed, developer handles everything
**Status as of 2026-08-21 (slice 3.2):** **weeks 1 and 2 are complete and committed**, and **3.1 is committed too** (`0244315`). **Week 3 is half done: slice 3.2 is complete and uncommitted**, awaiting the developer's test-diff review — so the working tree is 3.2 alone. **356 xUnit tests green (+18) and the web suite is 134 (+13).** 3.2 finished §5.1's expert surface: E1 filters the expert's **own** assignments by visa or cached plate, and E5's report upload is a panel on E2. **No migration and almost no new server code** — the search is one optional `q` parameter composed into the existing list statement, and E5 was one entry in `EXPERT_BUCKETS`, because 2.3 had already shipped `expert_report` with its doc type and PDF acceptance.

**The slice's real work was correcting design.md, not writing code.** §5.1 routed E1's search through `INext3Client.SearchClaims`, and two independent things made that unshippable. A NEXT3-wide search hands the expert claims they were never assigned — and the screen those results lead to carries a capture panel, so it is an invitation to attach photos to a stranger's visa, which is the precise failure this project exists to remove. And `ClaimSummary` has no policy number, no insured phone and no city, so a search hit **cannot** be upserted into the `claim` cache without inventing three fields NEXT3 owns. §5.1's search row and §6.1's claim-search row were both corrected (§6.1 said the operation "serves expert search and officer lookup", which would have contradicted §5.1 from the moment this shipped); `SearchClaims` stays on the port, still with no production caller, for the officer's lookup in 4.2. **Fifth slice running that design.md was corrected rather than quietly diverged from.** A recorded consequence: a cold cache has no plate to match, so such an assignment is findable by visa only — E1 says so on screen, and both halves are pinned by tests.

**The browser pass found the slice's only real bug, for the fourth slice running, and no test could have.** Search survived a reload exactly as the card asked — and was thrown away the instant the expert opened a claim, because E2's "← My claims" pointed at a bare `/expert`. An expert who searches a plate, opens the claim and comes back landed on their whole unfiltered history and had to retype it: on a phone, at a crash site, which is the entire situation the feature exists for. Nothing threw and every assertion passed, because **no test navigated**. E1 now carries `?q=` into each claim link and E2 hands it back, both pinned.

**Two more things worth knowing before reviewing.** **This slice adds the codebase's first hand-written analyzer suppression.** Case-insensitive search is `UPPER()` on both sides rather than a bare `Contains`, because nothing here configures a collation and a CS-collation deployment would otherwise change search behaviour with nothing going red — but CA1304/CA1311/CA1862 all assume the expression runs in .NET, and every fix they suggest (`ToUpper(CultureInfo)`, `Contains(string, StringComparison)`) has no SQL translation, so EF would throw on the expert's first search. Suppressed narrowly, at the statement, with the reason. Said honestly in the test: **it passes either way on LocalDB's default collation**, so the `UPPER()` is recorded, not proven. And **`expertKeys` gained a `lists()` prefix**: `invalidateQueries` matches by prefix, so once the search term is in the list key, invalidating `list()` no longer reaches `list('PLC-TEST')`. Verified by planting — with `list()` the new test goes red and every other web test stays green, which is exactly the failure mode. `useArrived` had the same bug and the card did not mention it.

**Verified in Chrome end to end against the fakes:** 11 keystrokes produced **1** request; `?q=` survived a reload *and* the claim round trip; expert Alpha searching Beta's plate and Beta's exact visa both returned nothing; whitespace-only returned the full list; 64 characters answered 200 and 65 answered `400 search_term_too_long`; `%` matched literally; and three report PDFs landed `not_applicable` / `PLACEHOLDER-DOC-05` / `application/pdf` / `.pdf` into *Expert documents* with `clientRef` = document id and outbox rows `sent` on attempt 1 — with the **filtered** list's media count going 2 → 3 without a reload, which is the `lists()` invalidation proven in a browser rather than only in jsdom.

**Status as of 2026-08-21 (earlier that day — slice 3.1):** **weeks 1 and 2 are complete and committed** (2.5 is `fb946a8`), and **week 3 has started: slice 3.1 is complete and uncommitted**, awaiting the developer's test-diff review — so the working tree is 3.1 alone. **338 xUnit tests green (+50) and the web suite is 121 (+49).** 3.1 put §5.1's last two expert artifacts through the pipeline 2.3 and 2.5 built: a **voice note** (record → `<audio controls>` playback-confirm → upload, §7.2 item 4) and a **damage diagram** (static SVG car, tap-to-mark, PNG via canvas — functional and unpolished, because §1 spent its polish on Option 2). Two new §7.1 buckets — `voice_note` under a new `MediaKind.Audio`, and `damage_diagram` registered as an *image* so the server's resolution floor applies to the export exactly as to a photograph — both `AllowUpload: false`, because both are produced in-app and there is no file to pick. One migration, db-reviewed. **Verified in Chrome end to end against the fakes:** the diagram landed `passed` / `PLACEHOLDER-DOC-07` / `.png` and the voice note `not_applicable` / `PLACEHOLDER-DOC-06` / `audio/webm` / 813,700 bytes, both into *Expert documents* with `clientRef` = document id and outbox rows reaching `sent` on attempt 1; all four cross-kind and picked-file refusals returned the right code and **wrote nothing** (exactly 2 documents after 4 refusals); Retake discarded without uploading; neither panel renders a file-picker control.

**Two things the browser pass could not settle, and one is a genuine gap.** **Playback was not verified.** The `<audio>` control renders, is bound to the right blob URL, and the recording is a real 813 KB WebM opening with the exact EBML magic the server sniffs — but this Chrome profile decodes no audio at all (a known-good 44-byte WAV stalls at `readyState: 0` identically), so *hearing* the note is the one thing left unproven. **It belongs on the week-6 device checklist.** Separately, a second recording failed loudly with "the recording did not complete" — which turned out to be the code working: the first recording's teardown had genuinely stopped the microphone tracks (`readyState: "ended"`), and the single-stream test stub handed back a dead stream where a real `getUserMedia` returns a fresh one.

**The playbook card and this file contradicted each other, and resolving it was the slice's design decision.** The card said route the diagram PNG through `useCapture.select`; §NEXT-SESSION said the diagram does *not* go through the clarity gate. `select` gates anything `image/*`, so both could not hold. HANDOFF won, via an explicit `select(file, origin, { photographic })` defaulted to true: a flat line drawing scored against `BlurVarianceThreshold` risks refusal with *"hold the phone still, let the camera focus on the damage"* — advice about a drawing, and 2.5's PDF-in-an-`<img>` bug in a new costume. **Deciding a file's treatment from its MIME type alone is the trap; the caller knows something the type does not.**

**The `db-reviewer` pass earned its keep for the fifth migration running, and again the finding was in the new code rather than the schema.** Every content-type comparison is case-insensitive, so `AUDIO/WEBM` is *accepted* — and the caller's spelling was then what got persisted, pushed to NEXT3, and matched **ordinally** by the blob-key extension map, filing a good voice note as `.bin` and hiding it from `WHERE content_type = 'audio/webm'`. Every accepting path now returns the **canonical** type the bytes were recognised as. **Accept loosely, store canonically.** The same pass caught that `Down()`'s loud failure — SQL Server validates a restored CHECK against existing rows — depends on the rollback running in a transaction: from a `--no-transaction` script the drop commits and the restore fails, leaving `document.bucket` unconstrained and every invented bucket string writable.

**A subfolder walked straight out of 2.5's reusability guard.** `diagram/` is the first nested folder in `media/`; the guard globbed `./*` with depth-fixed patterns, and a nested file reaches the expert module as `'../../expert/'`, which those patterns do not match at all. Both halves are hardened now and **verified by planting an import** — under the old pattern the violation passed silently, under the old glob the count went red. **A guard that quietly stops covering new code is worse than none, because it still reads green.**

**Status as of 2026-08-20 (slice 2.5):** **week 2 is complete.** Slices 2.1–2.4 are committed (`826e550`, `d099c93`, `79ff5d5`); slice 2.5 since committed as `fb946a8` — **weeks 1 and 2 are complete, working tree clean, 288 xUnit + 75 web tests green** (re-verified 2026-08-20). **Week-3 cards 3.1–3.4 expanded to verbatim prompts the same day** (`docs/build-playbook.md`); two decisions at expansion: E1 search is local to the expert's own assignments (design.md §5.1 corrected in 3.2 — a NEXT3-wide search would let an expert attach photos to claims never assigned to them), and 3.3's sandbox gate is closed, so it builds `RealNext3Client` against our own `docs/next3-openapi.yaml` and the mode stays `fake`. Next: slice 3.1. 2.5 built the capture component and clarity gate — the reusable pair that garage (5.1) and the Option 2 public page (5.3/6.1) must take unchanged, which is part of what design.md §11 spent to hold 8 weeks. E3 now sits under E2's Arrived panel with §7.1's four expert buckets, each running §7.2's resolution floor and Laplacian blur check before the E4 confirm screen, then posting through 2.3's streamed endpoint. **Verified in Chrome end to end against the fakes, across all four buckets:** sharp 1600×1200 PNGs passed at sharpness 19312 and drove `document` row → outbox row → dequeue → `sent`, with `size_bytes` matching the file on disk byte for byte and E1's media count reaching 4; a blurry one and an undersized one were both refused client-side with **no request leaving**; `origin` landed `captured` for the two car-photo buckets and `uploaded` for the two file-picked ones; a PDF stored `clarity_result = not_applicable` while a file-picked *image* stored `passed` (§7.2's "uploads included", holding in practice); Retake cleared the candidate and uploaded nothing; and the two car-photo buckets render **no file-picker control at all**. **Arrived was never pressed in the entire pass** — §5.1's ungated-capture interpretation demonstrated rather than asserted.

**The browser pass found the slice's only real bug, and the suite could not see it.** A PDF rendered into E4's `<img>` gives a broken-image icon on a black bar, over alt text reading *"The photo about to be sent to AXA"* — an expert asked to confirm a document they cannot see, miscalled a photo, on the one screen that exists so that a person looks at the file. Nothing throws: `naturalWidth` is simply 0, so every assertion still passed. E4 now names the file instead, pinned by a test. **A component test that never asks what the pixels did cannot see a broken image** — the same shape of gap as 2.4's timestamp bug, where the code's own arithmetic was asserted back to itself.

**The slice needed server code the playbook card did not mention, for a reason worth keeping:** a client-side gate needs client-side values, and CLAUDE.md's placeholder rule names *thresholds* explicitly — so 1024/768/100 could not be written into TypeScript, and a browser cannot read `appsettings.Placeholders.json`. Hence a new **anonymous `GET /api/config/media`**. Anonymous is the decision, not the shortcut: 5.3's public page runs this identical gate with no token. It sits outside `/public/*`, so §9.1's rate limits do not cover it — accepted because it touches no database and no user, and **recorded rather than assumed**. The same reasoning added one placeholder key: **a blur threshold is meaningless without the scale it was measured at**, since Laplacian variance scales with resolution and a bare `100` would pass and fail the same photo depending on the handset. `Clarity.BlurAnalysisMaxEdge` therefore lives beside the threshold in Appendix A, and design.md §7.2 was updated rather than diverged from — the 2.2 `visa_no` / 2.3 `outbox_message_id` precedent, now three for three.

**The 2.5 review point:** *the resolution floor had to be measured at native size, and that forced the API's shape.* Decoding a 48 MP photo into one RGBA array is ~192 MB — enough to kill the tab on the handset of an expert standing at a crash site — so the decoder returns native dimensions **plus** a bounded analysis buffer, and `assessClarity` takes the two separately. Had the floor been checked on the bounded buffer, a 1.1 MP photo would be refused as "too low-resolution": wrong, and unfixable from where the expert is standing. Also third time running for 1.5's lesson — the double-Confirm guard is a `useRef` latch, and removing it was verified to turn one upload into two, i.e. a duplicate photo under the visa and a second outbox row.

**Status as of 2026-08-20 (earlier that day — slice 2.4):** slices 2.2 and 2.3 committed (`d099c93`); 2.4 complete, since committed as `79ff5d5`. 283 xUnit tests green plus a **then-new 20-test web suite** (`npm test` in `src/Web` — Vitest + jsdom, stood up by 2.4 because the geolocation-denied path and the double-press guard are browser facts `dotnet test` cannot reach, and 2.5's clarity gate is entirely client-side). 2.4 is the first expert-facing screen work and the first *user action* to put a row in the outbox: E1 lists the expert's claims newest-first with media counts, E2 shows the claim and the **Arrived** button, and pressing it stamps `arrived_at` + coordinates and queues an `update_arrival` push in one transaction. Verified in the browser end to end against the fakes — arrival landed `sent` in `next3_outbox`; with location denied, the screen shows the blocking explanation and **no request leaves at all** (zero outbox rows, zero audit rows for that assignment). No migration: 2.1 created the arrival columns and 2.2 created `EnqueueArrival`.

**The `db-reviewer` pass earned its keep for the third slice running, and this time the worst finding was one the tests had *pinned*.** `ArrivalInfo` carried `DateOnly` + `TimeOnly` — §6.1's own words, written in 1.4 when nothing filled it. The moment 2.4 filled it from a UTC instant, the offset was discarded **at capture**: an expert arriving at 01:30 GST would be pushed to NEXT3 as arriving the previous calendar day, on the single field a claims dispute turns on (§9), in a row that can sit in the queue for 26 hours and that cannot be repaired from its own payload. It is a `DateTimeOffset` now; the split moved to `RealNext3Client` under a new `Next3:ArrivalTimeZone` placeholder, and design.md §12/Appendix A were updated rather than diverged from. The bitter part: the web layer had caught the *same* hazard on the display side that morning (bare timestamps read as local, "a four-hour error in GST that nobody would spot") and nobody connected it to the push side. **A lossy conversion belongs at the edge that knows the target format, never at the producer** — and a test that asserts the code's own arithmetic back to itself can never go red. The same pass also found that the new explicit transaction had to run inside `Database.CreateExecutionStrategy()`, or the day someone enables `EnableRetryOnFailure` (the standard Azure SQL fix, and §10 puts production there) Arrived breaks on *every* press with nothing in the suite to catch it; and that `ApiFixture` was proving both of this project's concurrency guarantees on LocalDB with `READ_COMMITTED_SNAPSHOT` **off**, while Azure SQL has it **on** — it is switched on now, all 283 pass under it, and the arrival guard was re-verified to discriminate under it.

**The 2.4 review point:** *"idempotent" had to be enforced in the `UPDATE`'s `WHERE` clause*, not by an `if`. Four simultaneous presses all pass a read-then-write check, and the consequence is not cosmetic — two `arrived_at` values and two pushes tell NEXT3 the expert arrived twice, at two different times. `WHERE arrived_at IS NULL` settles it; removing the clause was verified to turn the concurrency test from 1 arrival into 4. Because `ExecuteUpdate` runs outside `SaveChanges`, the endpoint opens the codebase's **only explicit transaction** so §4's one-transaction rule holds. A concurrency token on `ArrivedAt` — the 1.5/2.2 idiom — was rejected: EF applies a token to every write of the entity, so a concurrent E2 first-open would fail its `opened_at` save. **A guard that belongs to one transition should not be pinned to the whole row.** The same class of bug appears one layer up in the browser: `mutation.isPending` cannot stop a double tap in the same tick, because state has not landed yet — only a `useRef` latch does, and swapping it back was verified to turn one request into two.

**Status as of 2026-08-20 (earlier):** **Week 1 complete and committed** (1.1 scaffold + arch tests, 1.2 phone-OTP auth, 1.3 admin CRUD ×4 + append-only audit log + browser admin pages, 1.4 the five ports + fakes, 1.5 Option 2 schema + public-surface skeleton — all in git through commit `71023c9`). **Week 2: slice 2.1 (claim cache + expert assignments) is committed (`826e550`); slices 2.2 (the transactional outbox) and 2.3 (the server media pipeline) are complete and uncommitted, awaiting the developer's test-diff review.** 263 tests green; app boots on pure fakes. 2.2 is the load-bearing piece: every NEXT3 write now commits in the same transaction as the domain row that caused it, and a worker drains the queue with a `READPAST` stored procedure, retrying on §6.3's schedule and landing `sent` or `failed`. 2.3 is what finally puts a *file* into that queue — an expert streams a photo to `POST /api/expert/assignments/{id}/documents`, it lands in Blob, and a `document` row plus its outbox row commit together — and it makes §7.3's central rule real: **no blob is deleted before its outbox row is `sent`**, enforced by a structural join rather than by convention. **No client answers received; §8 communications still unsent.**

> ## ⏭️ NEXT SESSION: review and commit slice 3.2, then 3.3 (RealNext3Client + the OpenAPI contract)
> Build proceeds per `docs/build-playbook.md` — weeks 1 and 2 are ☑ and committed and so is 3.1 (`0244315`), **3.2 is ☑ and pending commit**; next unticked slice is **3.3**, whose card is already a verbatim prompt (week 3 was expanded 2026-08-20). Slice learnings are in the playbook's Notes lines.
>
> **Review 3.2 with these five in mind** (all in the playbook's 3.2 Notes and scope-decisions.md). **design.md changed in two places** — §5.1's search row and §6.1's claim-search row — because a NEXT3-wide expert search is an authorization hole and `ClaimSummary` cannot be cached; that is the slice's actual deliverable and the code is small. **The first hand-written analyzer suppression in the repo** sits on the search predicate (CA1304/CA1311/CA1862), because the analyzers' suggested fixes are untranslatable by EF — narrow, commented, and the CI-collation reasoning is recorded rather than proven. **`expertKeys.list()` became `lists()` + `list(q)`**, and both invalidation sites moved (`useRefreshAfterCapture` *and* `useArrived`, which the card did not name); the guard was verified by planting. **`ExpertAssignmentsPage` gained an optional `debounceMs` prop** — fake timers deadlock against @testing-library, so the debounce is tested the way `media/` tests its browser APIs, by injection. And **the test diff touches two existing files**: `ExpertAssignmentPage.test.tsx`'s `MEDIA_CONFIG` fixture gained an `expert_report` row (without it the new panel renders 2.5's "not configured" alert), and two capture-count assertions moved from 4/2 to 5/3 because E5 adds a panel with both controls — **no assertion was weakened or removed**, only the arithmetic. `ExpertFlows.SeedClaim` gained an optional `plateNo`, additively.
>
> **3.3 inherits from 3.2:** `INext3Client.SearchClaims` still has **no production caller** and now never will on the expert side — 4.2's officer lookup is its first one, so 3.3's contract tests are the only thing exercising it. The `document`/outbox path is unchanged. One known gap left open deliberately: **five capture panels label their controls identically** ("Take a photo" ×5, "Choose a file" ×3), which is a real screen-reader problem on E2 — not fixed here because `CapturePanel` is what 5.1 and 5.3/6.1 must reuse unchanged, and it is on the **week-6 device checklist**.
>
> **Review 3.1 with these five in mind** (all in the playbook's 3.1 Notes and scope-decisions.md). **`MediaValidation.CheckContent` gained an `out` parameter**, so ten existing call sites in `MediaValidationTests` grew an `out _` — the largest hunk in the test diff, and **no assertion was weakened or removed**. The parameter exists so that *what was validated is what gets stored*: a browser sends `audio/webm;codecs=opus`, and normalising in two places would give two answers that can disagree. **One placeholder key was added** — `Media.AudioContentTypes` — which is also the list the browser's recorder picks its format from, so no audio format literal exists in TypeScript; design.md §7.2 + Appendix A updated rather than diverged from, now four for four. **`useCapture.select` gained a third parameter**, defaulted so every existing caller and 5.1/5.3/6.1's future ones are unchanged. **`reusability.test.ts` was widened, not weakened** — glob to `./**/*`, patterns to depth-agnostic, plus a non-vacuity count and a "reaches into the subfolders" assertion; both halves were verified by planting an import. And **the migration is one check constraint**, regenerated from `MediaBuckets.All`, with a `Down()` caveat about non-transactional rollback written into the file.
>
> **3.2 inherits from 3.1:** the media pipeline now carries three kinds (image, document, audio) and `MediaKind` decides the allow-list. **E5 is nearly free** — `expert_report` already exists as a bucket with its doc type and PDF acceptance from 2.3, so it is one entry in `EXPERT_BUCKETS`. Note `CaptureSection` is no longer just that array: it also renders `VoicePanel` and `DiagramPanel`, so a change to the section's layout touches three call sites. **The media count helper is `countFor(bucket)`** now, shared by all three. Search adds `q` to `expertKeys.list`, and `useRefreshAfterCapture` is what all three panels invalidate through — if the key changes shape, capture stops refreshing the list it is looking at.
>
> > **One trap disarmed for 5.1:** `useCapture` reports `configLoaded` and `ready` as **two** flags. `findBucket` returns undefined both while the config is in flight and when a bucket is absent from §7.1's registry — collapsed, the second case renders "Loading photo settings…" for ever, with a clean network tab and an empty console. The garage buckets 5.1 adds need a `MediaBuckets` entry **and a migration** (`bucket` is check-constrained), so wiring the screen first is the likely order; the panel now says the section is unconfigured instead. **3.1 added a fourth rung to that ladder** for the diagram: a resolution floor the render cannot clear is said out loud, because otherwise a config change silently refuses every diagram as `image_too_small` and the drawing takes the blame.
>
> **Review 2.5 with these four in mind** (all in the playbook's 2.5 Notes and scope-decisions.md). **A new anonymous endpoint exists** (`GET /api/config/media`) — it is how the placeholder rule is honoured on the client, and its anonymity is 5.3's requirement pinned by a test, not an oversight; it is outside `/public/*` and therefore un-rate-limited, which is written down rather than assumed. **One placeholder key was added** (`Clarity.BlurAnalysisMaxEdge`) because a blur threshold without its measurement scale means a different thing on every handset; design.md §7.2 + Appendix A updated. **`withBearer` changed** — it now omits `Content-Type` for a `FormData` body, which is what lets the upload keep the refresh-on-401 retry instead of a second fetch path silently losing it. And **the test diff touches existing files**: `ExpertAssignmentPage.test.tsx`'s blanket `fetch` stub is now route-aware because E2 issues three GETs, and `ApiFixture` gained a mutable `ClarityOptions` monitor — no assertion was weakened or removed.
>
> **What 2.5 inherited from 2.4:** a **web test runner** (`npm test` — Vitest + jsdom + @testing-library; cleanup is wired by hand in `src/testing/setup.ts`, because with `globals: false` the library's own auto-cleanup never registers and every render stacks into the same document). **TanStack Query** is the server-state layer — logic in hooks under `src/Web/src/expert/`, components render, and `src/api/queryClient.ts` holds the defaults. E2 (`ExpertAssignmentPage.tsx`) is where the capture UI goes, and it must stay **ungated on arrival** (§5.1's recorded interpretation, asserted server-side by `ArrivedIsNotAPreconditionForCapture`). The upload endpoint and `MediaBuckets` from 2.3 are what the capture component posts to; note its multipart contract requires the metadata parts **before** the file part. `navigator.geolocation` and, by the same trick, any browser API can be stubbed with `Object.defineProperty` in both jsdom and a real Chrome tab — which is how the denied path was driven without a permission prompt blocking the automation.
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

## 7. ✅ DONE — the design document

`docs/design.md` was written from the brief that used to live here, and is the build's reference; `docs/build-playbook.md` carries the slice-by-slice cards. **The live next-session pointer is the banner at the top of this file, not this section.**

Two items came out of the manager's review of client doc v0.4 on 2026-08-19. Both are checked against `design.md` below rather than assumed:

- **Authorization scoping** (open question #42). The manager's sharpest point: how does the app know which claims an expert may see, and which declarations a garage may see? Largely already handled — `design.md` §9 enforces role plus resource-level checks server-side, and slice 3.2's expansion corrected §5.1 so expert search is **local to the expert's own assignments**, precisely because a NEXT3-wide search would let an expert attach photos to claims never assigned to them. **The residual dependency is on AXA:** if NEXT3 never exposes the expert-to-claim linkage, the app's own assignment records are the only source of truth for it, and that limitation belongs in the scope letter.
- **Offline capture** (open question #45). `design.md` keeps it out of scope, and §11 states plainly that there is **no remaining descope lever** — diagram polish and UAT round 2 are already spent holding 8 weeks with Broker Option 2 in. Client doc **v0.5 Q13 offers it to AXA at roughly one extra week**. If they say yes, there is nowhere for that week to come from. **Resolve this before the document is sent** — either withdraw the offer, or price it as a paid extension rather than absorbing it.

Two more from the same review, for the record: **image fraud/tamper detection appears nowhere in `design.md`** and is correctly excluded in v0.5 Q9 — keep it that way unless separately quoted. And **the clarity gate was already built in slice 2.5** (resolution floor + Laplacian blur + confirm screen, voice = playback-confirm only), so v0.5 Q14 asks the client to confirm something that already exists in code; if AXA answers "manual review queue" it is rework, not a choice.

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
3. **Send the NEXT3 OpenAPI proposal** (§6). Fastest way to force clarity on the biggest unknown. **Slice 3.3 writes it as `docs/next3-openapi.yaml`** — send that file.
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
  AXA-Motor-Claims-Questions-and-Costs-v0.5.docx   The client document (6pp), NOT yet sent
                            v0.5 = v0.4 + the manager review of 2026-08-19: authorization scoping,
                            photo rules/fraud, master data for all user types, and a new §5 asking
                            mobile-vs-PC parity, offline behaviour and clarity-check automation.
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
                            arrival happen exactly once — admin dev-injection endpoint, and E1's
                            local `q` search: no NEXT3 call, design.md §5.1 corrected in 3.2),
                            Modules/Broker (broker_request
                            + B3 create-link), Modules/PublicSurface (the ONLY unauthenticated surface:
                            public_link_token, /public/* endpoints, chained per-IP/per-token rate
                            limiter, body-size cap — may not reference Users or Next3, arch rule 2),
                            Integrations/ (the 5 ports + fakes: Next3 incl. IAssignmentSource, Email,
                            Push, Sms; FakeBehavior = shared latency/failure injection),
                            Modules/Media (§7's pipeline: the `document` table, MediaBuckets = §7.1's
                            matrix as data, ImageHeader + AudioHeader + MediaContentType (parameter
                            stripping; validated type = stored type) + MediaValidation = §7.2 item 5's
                            server re-validation, MediaUploadService = the streamed multipart endpoint's
                            blob-then-rows ordering, MediaBlobCleanupTask = §7.3's two sweeps,
                            MediaConfigEndpoints = the anonymous GET /api/config/media that serves
                            §7.2's thresholds + §7.1's bucket rows to the browser, so no client
                            value is ever written into TypeScript),
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
                            integration tests on LocalDB via WebApplicationFactory (356 tests;
                            READ_COMMITTED_SNAPSHOT is switched on to match Azure SQL).
                            Media/ holds TestImages (synthetic JPEG/PNG/PDF headers), TestAudio
                            (WebM/MP4/Ogg container headers, slice 3.1) and the
                            IBlobStore contract tests, whose Azurite half self-skips when the
                            emulator is not listening — so the suite needs no Docker
  Web/                      React 19 + TS + Vite. Router, localStorage JWT + refresh-on-401 client,
                            admin pages (login, per-kind profile list/form). Dev proxy → API :5180.
                            Slice 2.4 added: TanStack Query (api/queryClient.ts), role-based
                            post-login routing (api/session.ts — navigation only, server still
                            enforces), UTC normalisation for bare API timestamps (api/datetime.ts),
                            expert/ (api + query hooks + geolocation + useArrived — all E1/E2 logic),
                            pages/ExpertAssignments*.tsx (E1/E2), and a Vitest suite (`npm test`;
                            testing/setup.ts registers @testing-library's cleanup by hand).
                            Slice 2.5 added: media/ (§7.2's gate + §7.1's capture UI — clarity.ts is
                            the pure core, decode.ts wraps the browser's canvas behind an injectable
                            parameter, useCapture + CapturePanel + ClarityConfirm are E3/E4, upload.ts
                            builds the ordered multipart). media/ imports NOTHING from expert/, pages/,
                            admin/, session, tokens or the router — asserted by reusability.test.ts,
                            because 5.1's garage flow and 5.3's public page must reuse it unchanged.
                            Slice 3.1 added: media/recorder.ts + useVoiceNote.ts + VoicePanel.tsx
                            (§7.2 item 4's playback-confirm; the recorder is injected, never stubbed,
                            and takes its formats from the server) and media/diagram/ (regions.ts =
                            the car as data, marks.ts = pure tap state, car.tsx = the SVG that IS
                            what gets exported, render.ts = injectable canvas renderer at a fixed
                            size above §7.2's floor, DiagramPanel.tsx). reusability.test.ts now
                            globs `./**/*` with depth-agnostic patterns, so the subfolder is covered.
                            Slice 3.2 added: expert/useAssignmentSearch.ts (E1's search box state —
                            the URL is the source of truth, `?q=` survives a reload and the trip into
                            a claim and back; the debounce delay is injectable, because fake timers
                            deadlock against @testing-library). expertKeys grew a `lists()` prefix:
                            once the search term is in the list key, invalidating `list()` no longer
                            reaches the list on screen. E5 is one entry in EXPERT_BUCKETS.
                            134 web tests total
.claude/
  settings.json             Hook registrations (post-edit format check, on-stop test run)
  hooks/                    Guarded PowerShell hooks — no-op until the solution exists in week 1
  skills/scaffold-module/   Skill: scaffold an API module per design.md §5 pattern
  skills/add-ef-migration/  Skill: add + safety-review + apply an EF Core migration
  agents/db-reviewer.md     Read-only subagent auditing EF migrations
CLAUDE.md                   Domain glossary + conventions + design.md import (the per-session contract)
HANDOFF.md                  This file
```
