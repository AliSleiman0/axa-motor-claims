---
name: scaffold-module
description: Scaffold an API module (entity, EF config, migration, endpoints, tests) following the design.md §5 pattern
---

Scaffold the module named in $ARGUMENTS following `docs/design.md`. If no module name was given, ask for one.

Steps — do them in order, do not skip the reads:

1. **Read the spec first.** `docs/design.md` §5.x for this module (screens, state machine, what each transition writes) and every §4 table the module touches. If the module isn't in §5, stop — it's a change request (`docs/scope-decisions.md`), not a task.
2. **Entity + EF configuration** in `src/Api`: match the §4 column list exactly — `uniqueidentifier` PKs, `datetime2` UTC timestamps, state columns as check-constrained `nvarchar` enums. Note the table's Cache-vs-Authoritative classification in a code comment only if the code can't show it.
3. **Migration** via the `add-ef-migration` skill (generate → safety-review → apply).
4. **Endpoints** gated per the §2 role matrix — server-side authorization policy per endpoint group, resource-level ownership checks on top (a garage sees only its declarations, a broker only its requests).
5. **Tests** in `src/Api.Tests`, derived from the module's transition table: one test per transition (state change + every write/side effect in order) plus the illegal-transition cases. Outbox-writing transitions must assert the document row and outbox row commit in one transaction.
6. **Run `dotnet test`** and fix failures — but never weaken a test to pass it; if a test looks wrong, say so and stop.
7. **Stop before committing.** Summarize what was created and which design.md sections it implements; the developer reviews the test diff before any commit.
