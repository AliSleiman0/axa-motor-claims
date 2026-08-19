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

## Source layout (decided week 0; created in week 1)

- `AxaMotorClaims.sln` at repo root
- `src/Api` — .NET 10 API (+ outbox worker host); placeholders in `src/Api/appsettings.Placeholders.json`
- `src/Api.Tests` — xUnit test project
- `src/Web` — React + TypeScript (Vite), Capacitor shell around the same build
- Build/test commands: `dotnet build` / `dotnet test` at root; web: `npm run build` (tsc + ESLint + Vite, lint failures fail the build) and `npm run dev`, both in `src/Web`.
- EF migrations: `dotnet ef` is a local tool (`.config/dotnet-tools.json`); use the `add-ef-migration` skill.
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
