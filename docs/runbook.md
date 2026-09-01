# Runbook — AXA Motor Claims

Draft, slice 7.6 (design.md §10). Written for whoever is on call once the test (and later,
production) environment is live — including the developer at 2am, which is the actual audience for
a runbook.

## Start / stop / scale

- **Test environment** scales to zero when nobody is using it (`scripts/provision-azure.ps1
  -EnvName test`'s Container Apps min-replicas of 0). The first request after idle pays a cold
  start — usually a few seconds for the container, longer if Azure SQL's serverless tier has also
  auto-paused (see "Incident basics" below).
- **Manual scale-up**: `az containerapp update --resource-group <rg> --name <app> --min-replicas 1`
  — useful right before a demo or a UAT session so nobody eats the cold start.
- **Restart** (e.g. to pick up a config change without a new image): `az containerapp revision
  restart --resource-group <rg> --name <app> --revision <name>`, or simpler,
  `az containerapp update` with any no-op flag forces a new revision.
- **Stop entirely** (cost control, e.g. between UAT rounds): scale min and max replicas to 0.
  Nothing is deleted; the database and storage account are untouched.
- **Production** (once week 8 provisions it): `minReplicas: 1` always — no scale-to-zero, so there
  is always a warm instance. Promotion is a git tag push (see CI section below), never a manual
  `az containerapp update --image`.

## Config-key reference

Every setting is `Section:Key` in `appsettings.Placeholders.json` (design.md Appendix A is the
canonical list) and becomes `Section__Key` as a Container Apps environment variable or secret —
the double-underscore is .NET's own configuration-provider convention, not something this project
invented.

**Secrets** (Container Apps *secrets*, never plain env vars, never `appsettings.Test.json` — see
"why no Test json" below): `ConnectionStrings__Default`, `Auth__Jwt__SigningKey`,
`Blob__ConnectionString`, `Push__Vapid__PublicKey`, `Push__Vapid__PrivateKey`. If NEXT3 credentials
land (`Next3:Mode=real`), `Next3__ApiKey` / `Next3__OAuth__ClientSecret` / the Oracle password join
this list.

**Plain env vars** (visible in `az containerapp show`, not secret): `Blob__Mode`, `Push__Mode`,
`Push__Vapid__Subject`, `Auth__AppBaseUrl`, `Next3__Mode`, `Next3__AssignmentSource`.

**Why no `appsettings.Test.json`**: a JSON file loaded between the placeholder file and the real
environment variables would let a placeholder silently win over a real setting the same way slice
3.4's user-secrets bug did — CLAUDE.md and this slice's own card are both explicit that pure env
vars are the only sanctioned path for a deployed environment's real values.

## Reading A2 (the failed-push admin screen)

`/admin/outbox` (once the admin web UI exists) lists two kinds of trouble, colored differently:

- **Red, "Not moving"** — a row at `failed`. It has exhausted its retry schedule (below) and needs
  a human decision: press **Retry** (one more attempt, does not reset the count) or investigate
  `last_error`.
- **Amber, overdue** (slice 7.2) — a `pending` row whose `next_retry_at` is more than two poll
  intervals in the past. This is the signature of a stopped worker, not a bad push — check first
  whether the `outbox worker` (a `BackgroundService` inside the API host, not a separate process)
  is actually running, e.g. via a restart or a log check for its "Outbox pass failed" line.
- **The badge** in the nav counts `failed` rows plus overdue `pending` rows — not long-`pending`
  rows still working through their backoff, which is expected and self-clearing.

## Outbox semantics

- **8 attempts**, backoff **1 min → 5 min → 30 min → 2 h → 6 h ceiling**, reaching `failed` at
  roughly **26 h 36 m** after the first attempt. This number is pinned by a test
  (`Next3OutboxMessage`-adjacent tests) — if it changes, that was a deliberate retune (#33, NEXT3's
  maintenance windows), not drift.
- **A manual Retry from A2 buys exactly one more attempt, not a fresh 26-hour schedule** — the
  `attempts` counter is not reset. This is deliberate: it keeps the number honest ("this push has
  been tried N times") and stops a retry from quietly hiding a systemic problem for another day and
  a half.
- **`clientRef` is the idempotency key** on every push (#32) — NEXT3 has been observed rejecting a
  duplicate file submission directly, so a retry after a timeout is safe even when the first
  attempt actually succeeded server-side.
- **Never delete a blob before its outbox row reads `sent`** (§7.3) — if you are ever tempted to
  manually clean up storage, check the row's status first.

## The fake ↔ real NEXT3 switch

- `Next3:Mode = fake` (default everywhere, including production until #1 answers) — no live NEXT3
  connection is ever attempted; the API runs against `FakeNext3Client`'s seeded catalog.
- `Next3:Mode = real` flips to `RealNext3Client`, which validates its own config
  (`Next3OptionsValidator`) at boot and **refuses to start** if `Next3:BaseUrl`/credentials still
  carry a `PLACEHOLDER` marker — so a real-mode container that won't boot at all is almost always a
  missing secret, not a code bug.
- `Next3:AssignmentSource = fake | oracle-poll` is the separate switch for assignment delivery
  (slice 7.4); `oracle-poll`'s own options (`Next3:Oracle:*`) validate independently, the same
  shape.
- Neither switch has been flipped to its real value in any environment as of this slice — both are
  built, tested against a fake, and unexercised against a live target (recorded in
  `docs/scope-decisions.md`).

## OTP-from-logs (what `scripts/uat-seed.ps1` needs from you)

The fake SMS sender writes `FAKE SMS to <phone>: <message>` to standard output, which becomes
container logs in a deployed environment (there is no real SMS provider wired up yet — #7/#40).
To read them:

```
az containerapp logs show --resource-group <rg> --name <app> --tail 50 --follow
```

`--follow` streams new lines as they arrive, which is what you want while `uat-seed.ps1` is
prompting you for the next code — request the OTP/invite in one terminal, watch the log in another,
paste the 6-digit code or the invite-token line back into the script's prompt.

## Backup / restore

- **Azure SQL point-in-time restore (PITR)** covers the application database — Azure SQL keeps
  automatic backups per its own retention policy for the provisioned tier; a bad migration or a
  destructive manual query is recoverable by restoring to a point just before it, to a new database
  name, then reconciling.
- **The blob container is a transit buffer, not a backup target** (§3, §7.3) — NEXT3 is the system
  of record for media once a push reaches `sent`. Losing the storage account is not data loss for
  anything already pushed; it *is* data loss for anything still `queued`/`deferred`, which is
  exactly why §7.3 never deletes a blob before its push is confirmed.
- **No backup story exists yet for NEXT3 itself** — it is AXA's system, outside this project's
  scope.

## Incident basics

- **"Nothing is reaching NEXT3"** — check A2 for `failed`/overdue rows first (see above). If the
  badge is climbing and the worker's own log shows no "pass failed" lines, check whether the API
  container itself is even running (a scaled-to-zero test environment with no recent traffic can
  look identical to a stuck worker from the outside).
- **"Readiness is flapping" (`/health/ready` alternating 200/503)** — very likely Azure SQL
  serverless's auto-pause/resume cycle on the test tier, not a real outage: a paused database takes
  a few seconds to resume on first connection, during which `CanConnectAsync` in `/health/ready`
  legitimately fails. Give the readiness probe's `initialDelaySeconds` generous headroom for
  exactly this reason (`scripts/provision-azure.ps1`'s comment on the same trap). Production's
  General Purpose tier does not auto-pause, so this is a test-only pattern to recognize and not
  panic about.
- **"An expert stopped getting push notifications"** — check `docs/oem-push-guidance.md` first
  (Android OEM battery-saver behavior is the most common real-world cause, not a server bug); then
  check the device-token registry for a revoked row (slice 7.2's eviction/deactivation rules).
- **"A supplier can't log in and I don't know why"** — check `app_user.status`: `inactive` means an
  admin deactivated them (reversible only by re-creating, per §5.4); `sync_blocked` (slice 7.5)
  means the NEXT3 master-data sync no longer sees them as an in-network supplier, and it self-heals
  the next time they reappear in that list — no manual action needed or possible for that status.

## Not yet answered, and why this runbook doesn't pretend otherwise

Open questions #37 (Azure resource-group access), #41 (one environment or two), and #21 (InfoSec
review scope) are all still `TBC` — nothing in this runbook, or in the DoD of the slice that
produced it, depends on any of them being answered first (design.md §10's own instruction).
