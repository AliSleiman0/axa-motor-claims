# CLAUDE.md — AXA Motor Claim Management

Read `HANDOFF.md` first. It holds current status and next actions.

**The build contract is `docs/design.md`** — 12 sections: scope, roles, architecture, data model, module state machines, NEXT3 integration, media pipeline, notifications, security, environments, week plan, blocked decisions. It is imported below so it is in context every session. Implement what it says; when it is silent, prefer the smaller interpretation and record it in `docs/scope-decisions.md`.

@docs/design.md

## How to build (spec-driven loop)

Work proceeds **slice by slice per `docs/build-playbook.md`** — pick the next unticked slice, use its prompt, meet its definition of done.

1. **Plan against the design doc first.** Before implementing a module, read its `design.md` §5.x and the §4 tables it touches; state the files, edge cases, and test plan; then implement.
2. **Test-first on the four pure-logic cores:** declaration state machine (§5.2 transition table), outbox worker (§6.3 retry/backoff/idempotency), clarity gate (§7.2), Option 2 token lifecycle (§9.1). Write the failing tests from the design doc's tables, then implement to green.
3. **Run the tests before any commit.** Never change a test to make it pass without saying so explicitly — the developer reviews test diffs at every gate.
4. **Placeholder discipline:** any client-specific value (insurance types, recipients, NEXT3 codes, thresholds) exists only as a named key in the placeholder config (design.md Appendix A). A client literal anywhere else is a bug.

## Recurring bug classes — check these before declaring a slice done

Each of these was caught by review, not by the author, and at least twice (slice numbers in `docs/build-playbook.md` Notes):

- **"This may only happen once" belongs in the schema or the `WHERE`, never in an `if`.** A read-then-write on a state column is not a state machine: four simultaneous requests all pass the read. Use a unique index, a concurrency token, or `UPDATE … WHERE state = @expected`, and **verify by removing the guard** — the parallel test must go red. (1.5 token lock, 2.2 outbox lease, 2.4 Arrived, 3.4 subscriptions, 4.1 `state` token.)
- **`HttpClient` reports its own timeout as `TaskCanceledException`**, which derives from `OperationCanceledException` — so a catch filter naming `TimeoutException`, or one excluding `OperationCanceledException`, silently misroutes it. Discriminate on `ct.IsCancellationRequested`, convert to `TimeoutException` at the adapter edge, and pin both directions. **Check every HTTP adapter, not just the one you are writing** — 3.3 fixed this and 3.4 reintroduced it the next day.
- **A lesson learned in one adapter is not learned until it is checked in the others.** When a slice fixes a class of bug, grep the codebase for siblings before closing.
- **Accept loosely, store canonically.** Case-insensitive and parameter-tolerant comparisons are fine; what gets persisted, pushed and matched ordinally must be the canonical value the bytes were recognised as (3.1).
- **Deciding a file's treatment from its MIME type alone is the trap** — the caller knows something the type does not (`photographic` on `useCapture.select`, 3.1; PDF-in-an-`<img>`, 2.5).
- **A test that asserts the code's own arithmetic back to itself can never go red** (2.4 arrival split, 2.5 variance). Work the expected value out by hand.
- **A guard that quietly stops covering new code is worse than none** — nested folders walked out of the reusability glob (3.1); an arch rule sat green while its namespace was empty (1.5). Pair every guard with a non-vacuity assertion.
- **Review the db-reviewer's findings on the *new code*, not just the schema** — in six of seven migration slices the worst bug was in the adjacent code it read.

## Source layout (decided week 0; created in week 1)

- `AxaMotorClaims.sln` at repo root
- `src/Api` — .NET 10 API (+ outbox worker host); placeholders in `src/Api/appsettings.Placeholders.json`
- `src/Api.Tests` — xUnit test project
- `src/Web` — React + TypeScript (Vite), Capacitor shell around the same build
- Build/test commands: `dotnet build` / `dotnet test` at root; web: `npm run build` (tsc + ESLint + Vite, lint failures fail the build), `npm test` (Vitest + jsdom, added slice 2.4) and `npm run dev`, all in `src/Web`. The Stop hook runs `dotnet test` only — run `npm test` yourself after touching `src/Web`. **jsdom has no canvas and no `canvas` package is installed on purpose** — image logic belongs in a pure function over pixel buffers with the browser call behind an injectable parameter (`src/Web/src/media/clarity.ts` and `decode.ts` are the pattern), because anything that needs a real `createImageBitmap` can only be proven in a browser.
- EF migrations: `dotnet ef` is a local tool (`.config/dotnet-tools.json`); use the `add-ef-migration` skill.
- **Blob storage — Azurite for local dev.** `Blob:Mode` is `fake` (in-memory) everywhere by default, so **`dotnet test` needs nothing running**. `dotnet run` sets `Blob__Mode=azure` via `launchSettings.json` and talks to Azurite on `UseDevelopmentStorage=true`. Start it either way:
  - npm (no Docker Desktop needed): `npx --yes -p azurite azurite-blob --silent --skipApiVersionCheck --location <scratch-dir>`
  - Docker: `docker run -d --name azurite -p 10000:10000 mcr.microsoft.com/azure-storage/azurite azurite-blob --blobHost 0.0.0.0 --skipApiVersionCheck`

  `--skipApiVersionCheck` is not optional: the Azure SDK's default API version runs ahead of the latest Azurite release, and without it every call fails with `InvalidHeaderValue`. Not running Azurite? Set `Blob__Mode=fake`. `BlobStoreContractTests` asserts the same contract against both stores and **skips the Azurite half when nothing is listening on port 10000**, so the real adapter is covered when the emulator is up without the suite depending on it.
- **Web push — VAPID keys never live in the repo.** `Push:Mode` is `fake` everywhere by default, so **`dotnet test` and `npm test` need no keys at all** and `appsettings.Placeholders.json` holds only `PLACEHOLDER-*` values. A VAPID private key is a real credential — whoever holds it can send notifications browsers accept as coming from AXA — so for a real browser pass, generate a pair and put it in user-secrets:
  ```
  npx --yes web-push generate-vapid-keys
  dotnet user-secrets set "Push:Vapid:Subject"    "mailto:you@example.com" --project src/Api
  dotnet user-secrets set "Push:Vapid:PublicKey"  "<the public key>"       --project src/Api
  dotnet user-secrets set "Push:Vapid:PrivateKey" "<the private key>"      --project src/Api
  ```
  Then run with `Push__Mode=webpush`. In deployment the same three values are Container Apps secrets (§10) — **never** appsettings, never a commit. Keys are per-environment: rotating them silently invalidates every stored subscription, because a browser subscribes against one specific public key.
- Line endings are **LF everywhere** (`.editorconfig` + `.gitattributes`); the Write tool emits LF, so this keeps the `dotnet format` hook quiet. Don't switch to CRLF.

## Domain glossary

| Term | Meaning |
|---|---|
| **NEXT3** | AXA's claims core system. Source of truth for claims. The app reads from and writes to it via client-provided API endpoints. |
| **Visa number** | The claim reference opened in NEXT3. Every photo and document must land under the correct visa. The whole project exists because this linking is done manually by email today. |
| **TP** | Third party — the other party in the accident. TP documents and TP car photos are separate buckets from the insured's. |
| **Survey** | The garage-initiated path: customer goes to a garage instead of calling for an expert. Also the name of the NEXT3 folder the approval lands in. |
| **Expert** | AXA's field assessor, dispatched to the accident site. |
| **Claim officer** | AXA staff who approves or rejects a garage's declaration and links it to a visa. |
| **Broker** | Sells new policies. Unrelated to the claims flow — a separate module in the same app. |
| **IRIS code** | Broker identifier. Source list not yet supplied by AXA. |
| **Arrived** | Button in the expert flow that writes arrival date, time and location back to NEXT3. |

## Conventions

- **Stack:** .NET 10 API + Azure SQL (EF Core) + React/TypeScript PWA wrapped in Capacitor, on Azure Container Apps, files in Azure Blob. Chosen for developer fluency and because AXA is a Microsoft/Azure shop that will support this after handover.
- **Stored procedures surgically, not everywhere.** EF Core migrations + LINQ for the app's own domain — requirements are still moving and iteration speed wins. Reserve procs for the outbox dequeue, direct writes into NEXT3's database, and bulk/reporting queries.
- **All NEXT3 writes go through the transactional outbox.** Document row and outbox row commit in one transaction. Pushes must be idempotent via a stable `clientRef`. **Never delete a blob before its push is confirmed `sent`.**
- **The NEXT3 client always sits behind an interface**, with a fake implementation kept working. Never let the build block on AXA's API availability, and never call NEXT3 directly from feature code.
- **Car photos are capture-only** — upload must be disabled for those buckets (Insured Car Photo, TP Car Photo, and the garage equivalent). Documents allow both upload and capture.
- **Do not invent client data.** Insurance types, email routing recipients, NEXT3 field names and document-type codes are all unanswered (`docs/open-questions.md`). Use obvious placeholders and keep them in one config file.
- Test the risky parts by hand on real devices: camera capture, push delivery, upload retry. Automated tests do not catch what breaks there.

## Guardrails

- 2 months, $5,000, solo. Every unplanned week is unpaid. When a request is ambiguous, prefer the smaller interpretation and record it in `docs/scope-decisions.md`.
- Anything not listed as in-scope in `docs/scope-decisions.md` is a change request, not a task.
