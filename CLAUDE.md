# CLAUDE.md — AXA Motor Claim Management

Read `HANDOFF.md` first. It holds current status and next actions.

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

- **Stack:** Next.js (App Router) PWA + Postgres + Prisma + S3-compatible blob storage. TypeScript everywhere. Boring, common frameworks by deliberate choice.
- **The NEXT3 client always sits behind an interface**, with a fake implementation kept working. Never let the build block on AXA's API availability, and never call NEXT3 directly from feature code.
- **Car photos are capture-only** — upload must be disabled for those buckets (Insured Car Photo, TP Car Photo, and the garage equivalent). Documents allow both upload and capture.
- **Do not invent client data.** Insurance types, email routing recipients, NEXT3 field names and document-type codes are all unanswered (`docs/open-questions.md`). Use obvious placeholders and keep them in one config file.
- Test the risky parts by hand on real devices: camera capture, push delivery, upload retry. Automated tests do not catch what breaks there.

## Guardrails

- 2 months, $5,000, solo. Every unplanned week is unpaid. When a request is ambiguous, prefer the smaller interpretation and record it in `docs/scope-decisions.md`.
- Anything not listed as in-scope in `docs/scope-decisions.md` is a change request, not a task.
