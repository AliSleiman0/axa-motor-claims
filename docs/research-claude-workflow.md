# research-claude-workflow.md — Building this product with Claude Code

**Status:** research findings, 2026-08-19. Companion to `design.md`; assumes its §11 week plan.
**Question answered:** what is the fastest / most efficient way to deliver this product with Claude Code, driving the build from `design.md`?

---

## The conclusion up front

The single highest-leverage asset already exists: a decisive, numbered design doc. The fastest path is **spec-driven development** — `design.md` is the contract, Claude Code implements one module at a time against it in plan-then-execute cycles, and a test suite over the state machines and outbox is the verification loop that lets Claude iterate without being babysat. Research consistently reports 3–10× first-pass success on non-trivial tasks when working from a spec versus prompt-by-prompt building, and Claude Code rates "excellent" on exactly this stack: C#/ASP.NET Core, EF Core migrations, multi-file refactoring, run-tests-and-iterate.

Two project-specific accelerators make this setup unusually favorable:

1. **The fake NEXT3 client** means Claude can build and *verify* end-to-end every week with zero client dependencies — including driving the PWA through real flows with the Chrome integration.
2. **The placeholder config rule** (no invented client data outside `appsettings.Placeholders.json`) is mechanically checkable — Claude can be held to it by grep, not by hope.

The build is not the schedule risk. The client-side unknowns (#1 sandbox, #21 InfoSec, #31 NEXT3 ownership) remain the risks, exactly as `design.md` §12 records.

---

## Week 0 — the setup (half a day, before week 1 of `design.md` §11)

"Week 0" is not a calendar week: it is the pre-kickoff scaffolding, done alongside the outstanding client actions in HANDOFF §8, before week 1 coding starts.

1. **Wire `design.md` into every session.** Extend the repo `CLAUDE.md` (keep it under ~200 lines — models follow short instruction files far more reliably) with an `@docs/design.md` import so the spec loads automatically, plus the non-negotiables restated as one-liners: all NEXT3 writes through the outbox; never delete a blob before `sent`; no client data invented outside the placeholder config; capture-only buckets. Add build/test/run commands as they stabilize in week 1.
2. **Create 2–3 project skills** (`.claude/skills/<name>/SKILL.md`) for procedures repeated 5+ times:
   - `scaffold-module` — entity → EF migration → endpoints → tests, following the `design.md` §5 module pattern
   - `add-ef-migration` — generate, review the migration file, apply
   - `deploy` — added once the §10 pipeline exists
3. **Add hooks** (`.claude/settings.json`) so quality is enforced mechanically, not by asking nicely: `dotnet format`/build check after edits, and a stop-hook running the test suite so Claude sees failures and fixes them before ending a turn. A .NET-focused study reports project-specific convention files (EF Core, DI, test naming) cut review corrections by ~40–50%.
4. **Optional, cheap:** a read-only `db-reviewer` subagent (`.claude/agents/db-reviewer.md`) that audits every migration for data loss, rollback safety, and missing indexes before it is applied.

---

## The weekly rhythm (maps onto `design.md` §11)

- **Plan first, per module.** Open plan mode, point Claude at the relevant `design.md` section ("Implement the declaration state machine per §5.2 — list files, edge cases, and the test plan first"), review the plan, then execute. The plan-mode review costs minutes; unwinding a wrong build costs days — and a solo builder has no one else to catch drift.
- **Test-first on the risky cores.** The declaration state machine, the outbox worker (retry/backoff/idempotency), the clarity gate, and the Option 2 token lifecycle are pure logic and perfectly testable. Have Claude write failing tests from the design doc's transition tables, then implement to green. This is also the defence against the known failure mode where models edit tests to pass instead of fixing code: **review test diffs at every gate.**
- **Verify against the fake.** Weekly end-to-end passes run entirely on the fake NEXT3 client and fake senders. Use Chrome automation to drive the PWA through the real flows (assignment popup → Arrived → capture → clarity → outbox; declaration submit → approve → Survey push).
- **Quality gates.** `/code-review high` at each week's end on the week's diff. Before handover (week 8): `/code-review ultra` (deep multi-agent cloud review) plus the `/security-review` skill focused on the public Option 2 surface — that is the pre-InfoSec pass for #21, run before AXA's reviewers ever see it.
- **Sessions and memory.** Name and resume sessions per module; let auto-memory accumulate gotchas; keep `HANDOFF.md` current as the cross-session state file (already the practice in this repo).
- **CI.** GitHub Actions does the deterministic work (build, test, image, deploy — `design.md` §10) without Claude in the loop; `/install-github-app` optionally adds `@claude` PR review, but for a solo repo the local `/code-review` before push is cheaper and sufficient.

---

## What NOT to do

- **No autonomous overnight loops ("Ralph" technique) for feature work.** Stop-hook loops that force Claude to keep iterating for hours genuinely work for well-verified mechanical tasks — but on a fixed-price contract where invented client data or a broken blob-deletion rule is a liability, human review at phase gates is the safeguard every serious writeup comes back to. If used at all: only on fully test-fenced chores ("make all tests pass after this refactor"), never on new surface area.
- **No multi-agent workflows for normal features.** Orchestrated agent fan-out pays off for codebase-wide refactors, broad audits, and research — not for building one module at a time. For this build it mostly adds token cost and coordination overhead.
- **No top-tier model for routine work.** Opus-class for architecture and hard debugging; Sonnet-class for CRUD/scaffolding; fast mode only for tight interactive debugging bursts. Wiring a DTO does not need the expensive model.

---

## Week-by-week fit with `design.md` §11

| Wk | Claude Code emphasis |
|---|---|
| 0 | Setup above (CLAUDE.md import, skills, hooks, db-reviewer) |
| 1 | Plan mode over §4 + §5.4 → scaffold via skills; tests on auth/OTP + outbox write path from day one |
| 2–3 | Expert module: TDD on clarity gate + outbox; Chrome-driven PWA verification against the fake |
| 4 | Declaration state machine test-first from the §5.2 transition table; `/code-review high` before the client demo |
| 5–6 | Broker Options 1+2; grep-gate on placeholder config; device checkpoint is manual (hand-test push + camera — Claude can't do this part) |
| 7 | `/security-review` + `/code-review ultra` on the public surface; fix pass |
| 8 | Final review pass, deploy via CI, handover docs (Claude drafts runbook from §10) |

---

## Sources

- [Augment Code — Claude Code for spec-driven development](https://www.augmentcode.com/guides/claude-code-spec-driven-development)
- [DataCamp — Spec-driven development with Claude Code](https://www.datacamp.com/tutorial/spec-driven-development-with-claude-code)
- [Thoughtminds — Spec-driven development guide](https://thoughtminds.ai/blog/spec-driven-development-using-claude-code)
- [codewithmukesh — 20 advanced Claude Code tips for .NET developers](https://codewithmukesh.com/blog/claude-code-tips-advanced/)
- [Talk Think Do — Claude Code for .NET developers, 2026 guide](https://talkthinkdo.com/guides/development-practice/claude-code-dotnet-developers-guide/)
- [Gil Ricardo — Claude Code vs Cursor vs Copilot for .NET, 2026](https://www.gilricardo.com/blog/claude-code-vs-cursor-vs-copilot-dotnet-2026)
- [claudefa.st — Ralph Wiggum technique](https://claudefa.st/blog/guide/mechanics/ralph-wiggum-technique)
- [paddo.dev — Autonomous loops for Claude Code](https://paddo.dev/blog/ralph-wiggum-autonomous-loops/)
- Claude Code docs: memory/CLAUDE.md imports, hooks guide, skills, subagents, workflows, code review, GitHub Actions, Chrome integration
