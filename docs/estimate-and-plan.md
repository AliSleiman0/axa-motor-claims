# Plan, hosting and running cost

## 8-week plan

| Week | Work |
|---|---|
| 1 | Repo, auth (phone OTP), RBAC, admin CRUD ×4 profiles, **NEXT3 client behind an interface with a fake implementation** |
| 2 | Expert: claim list/detail, Arrived + geolocation, capture component, 4 document buckets, upload pipeline |
| 3 | Expert: voice note, car diagram, quality check + retry, visa/plate search, report upload. **Swap fake → real NEXT3** |
| 4 | Garage declaration + submit; Claim Officer review, visa search, approve/reject, approval image; web push. **Client demo → 30% payment** |
| 5 | Post-approval repair uploads, rejection paths, Broker Option 1 + email routing |
| 6 | Hardening: retry queue, audit log, file size limits, error states. Real-device pass on 3–4 handsets |
| 7 | **UAT round 1** + fixes |
| 8 | UAT round 2, deploy, handover docs, training session |

Weeks 7–8 are the risk window. Insurer UAT generates change requests — the two-round cap is the defence.

## Hosting

**Recommendation: cloud for dev/UAT, push for cloud in production, architected so on-prem remains possible.**

On-prem costs 2–3 weeks this timeline doesn't have: VPN access, their infra team's deployment windows, no CI/CD, no direct log access, debugging by proxy.

Three things force the decision, not developer preference:

1. **Where the NEXT3 API lives.** If internal-only (likely for a claims core system), a cloud app needs one of: internet exposure with IP allowlist + mTLS, site-to-site VPN, or an **outbound tunnel** (Cloudflare Tunnel / Azure Relay) installed on their side. **Propose the tunnel first** — cheapest, no inbound firewall rules.
2. **AXA Group data residency policy.** AXA Group is Azure-heavy; "our Azure tenant" is the likely counter-offer.
3. **Who pays and who operates it after handover.** This should actually decide it.

## Monthly running cost

Volumes are not in the BRD. Assumed **~100 claims/day, ~15 photos each at ~1.5 MB, ~200 users**. Revise once question #20 is answered.

### Option A — lean cloud (recommended)

| Item | Monthly |
|---|---|
| Vercel Pro (commercial use requires it) | $20 |
| Neon Postgres (Launch) | $19 |
| Cloudflare R2 storage (no egress fees) | $2 → ~$12 by month 12 |
| Email (SES / Resend) | $0 – 20 |
| Sentry / monitoring | $0 – 26 |
| SMS OTP gateway (MENA rates ~$0.05–0.10/msg) | $20 – 60 |
| Domain | ~$1.50 |
| **Total** | **≈ $60 – 160 / month** |

### Option B — AXA Azure tenant (if mandated)

App Service P1v3 ~$120 + Postgres Flexible B2s ~$80 + Blob ~$20 + Front Door/WAF ~$40 + Log Analytics ~$20 → **≈ $280 – 400 / month**, paid by AXA. Add ~1 week for their landing-zone process.

### Option C — on-prem

No recurring cloud bill, but their VM/SAN/backup cost plus **2–3 weeks of developer time, unpaid**.

### Two points that matter more than the numbers

- **The biggest storage variable is question #4, not an estimate.** If NEXT3 is the permanent home for photos and this app is a transit buffer, files are kept for days and storage stays ~$2/month forever. If this app is the system of record, it grows ~68 GB/month and never stops.
- **All accounts in AXA's name, on AXA's card, from day one.** On $5k, chasing reimbursements costs more than the hosting.

## For reference — what this scope is actually worth

Not for the client; for the developer's own calibration.

| Basis | Effort |
|---|---|
| Full BRD, team of ~6 | ~300–430 person-days, 6–8 months |
| Full BRD, solo with AI assistance | ~200–280 person-days, 9–13 months |
| **Committed** | **2 months, $5,000, solo** |

Roughly **60–70% of the BRD** fits the commitment at deliverable quality. The strategy is scope control (`scope-decisions.md`), not renegotiation.
