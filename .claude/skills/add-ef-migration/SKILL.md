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
3. Apply locally: `dotnet ef database update --project src/Api`
4. Run `dotnet test`.
5. **Never hand-edit a migration that has already been applied anywhere** — add a new migration instead.
6. For a deeper check on risky migrations, ask the `db-reviewer` agent to audit the file.
