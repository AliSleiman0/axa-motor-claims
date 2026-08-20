---
name: add-ef-migration
description: Add an EF Core migration, safety-review the generated file, and apply it locally
---

Add an EF Core migration named in $ARGUMENTS (PascalCase, e.g. `AddDeclarationTable`). If no name was given, derive one from the pending model change and confirm it.

1. From the repo root: `dotnet ef migrations add <Name> --project src/Api`
2. **Review the generated migration file before applying** — the checklist:
   - No data loss: no dropped column/table that holds data without an explicit, stated decision.
   - `Down()` actually reverses `Up()`.
   - New foreign keys have indexes; new state/enum columns have their check constraint.
   - No unintended column type or nullability changes (EF sometimes rewrites more than the change you made).
   - `next3_outbox` must remain byte-compatible with design.md §4 — its schema is contractual.
3. **Run the `db-reviewer` agent on the generated migration + snapshot — every migration, not just risky ones.** It found the worst bug in three of the first four migration slices (the outbox two-owner window, the retention sweep's batch commit, the `DateOnly` arrival split), each one invisible to the tests. Fix its findings before applying; regenerate rather than stack if the migration is still uncommitted.
4. Apply locally: `dotnet ef database update --project src/Api`
5. Run `dotnet test`.
6. **Never hand-edit a migration that has already been applied anywhere** — add a new migration instead.
