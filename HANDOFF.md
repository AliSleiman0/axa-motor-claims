# HANDOFF — read this first

**Project:** AXA Middle East — Mobile Application for Motor Claim Management
**Developer:** solo (Ali Sleiman)
**Commitment:** 2 months, $5,000 fixed, developer handles everything
**Status as of 2026-08-18:** Pre-kickoff. BRD analysed. **No code written yet.** No client answers received yet.

---

## 1. What this project is

AXA Middle East sent a BRD (`docs/source/`). Today their motor-claim photos travel by **email**: experts photograph accident damage at the roadside and email it in; one staff member ("Joanna") manually downloads thousands of emails and re-uploads each photo under the right visa number in **NEXT3** (AXA's claims core system). Expert reports take **2+ weeks**, so the claims team can't make any preliminary assessment or answer the insured / third party in the meantime.

The app replaces that email path: field users capture photos in-app, and the app pushes them straight into NEXT3 under the correct visa number.

Four profiles: **Expert**, **Garage**, **Claim Officer**, **Broker**.

Read `docs/BRD-extracted-text.md` for the full source text and `docs/diagrams/` for the client's four flowcharts. Both are extracted from the original .docx, which is preserved in `docs/source/`.

---

## 2. Decisions already made (do not re-litigate)

| Decision | Rationale |
|---|---|
| **PWA, not native apps** | The BRD says *"installed on mobile or logged in PC"* — a PWA satisfies this from one codebase. Removes iOS build chain, Mac requirement, two app-store reviews, MDM distribution, two-platform device testing. Saves ~3–4 weeks of the 8. Camera capture, forced in-app capture (`getUserMedia`), voice (`MediaRecorder`) and geolocation all work in mobile browsers. |
| **Next.js + Postgres + Prisma + S3-compatible blob + Vercel** | One language across the stack. Maximum AI-assisted throughput; avoid niche frameworks. |
| **Cloud hosting, containerized so on-prem stays possible** | On-prem costs 2–3 weeks (VPN, their deploy windows, no CI/CD, no log access) that this timeline does not have. Deployment target written into scope as "client-provided environment, containerized". |
| **NEXT3 client behind an interface + fake implementation, built day 1** | Lets all four modules be built while AXA is still wiring up API access. This is the single most important architectural decision for hitting the deadline. |
| **Clarity check = resolution + blur-variance threshold + user confirm** | The BRD says *"image visibility and voice clarity must be ensured"* with no definition. **Not ML.** Half a day, not two weeks. Needs written client sign-off (open question #9). |
| **Broker Option 2 excluded from this phase** | It is a second product surface (public flow for unauthenticated end-customers). Primary negotiating chip. |

Full in/out list: `docs/scope-decisions.md`.

---

## 3. IMMEDIATE next actions (in order)

These are **not coding tasks**. Nothing below the line matters until these are done.

1. **Send the client the blocking questions** — the 8 in `docs/open-questions.md` § Blocking. Especially #1 (API spec + sandbox credentials), #2 (network reachability), #21 (InfoSec review / pen test), #23 (confirm PWA is acceptable delivery).
2. **Send the one-page scope letter** with the exclusions from `docs/scope-decisions.md` and the client dependencies with dates. Get it acknowledged by email — that's sufficient. *Not yet drafted — this is the first thing to write next session.*
3. **Agree payment milestones**: 40% up front / 30% at week-4 demo / 30% at handover.
4. **Confirm all infra accounts are in AXA's name, on AXA's card.** On a $5k budget, hosting and SMS on a personal card is unrecoverable.
5. Then, and only then, scaffold the app (week 1 of the plan in `docs/estimate-and-plan.md`).

**Deadline risk to state out loud to the client now:** every day the NEXT3 sandbox credentials slip, the delivery date moves day-for-day. Say it before it happens, not after.

---

## 4. The three things most likely to break the 2 months

1. **AXA Group InfoSec review / pen test** — timing unknown, not on your clock, and not yet asked about (question #21).
2. **NEXT3 API access slipping** — mitigated by the fake-implementation-first architecture, but only up to ~week 3.
3. **UAT scope creep in weeks 7–8** — cap at **two UAT rounds** in writing; anything beyond agreed scope is a separate quote.

---

## 5. Repo map

```
docs/
  BRD-extracted-text.md     Full text extracted from the client .docx
  source/                   Original client .docx (unmodified)
  diagrams/                 The client's 4 flowcharts, extracted as PNG
  scope-decisions.md        In scope / out of scope / simplifications
  open-questions.md         27 questions for the client, prioritised
  estimate-and-plan.md      8-week plan, hosting options, monthly cost
CLAUDE.md                   Domain glossary + conventions for AI sessions
HANDOFF.md                  This file
```

---

## 6. Context an AI session won't infer

- The **$5k / 2-month commitment is already made.** Independent estimates for this full BRD land at 9–13 months solo, or ~300–430 person-days with a team. The commitment stands; the strategy is aggressive scope control, not renegotiation. Don't spend the user's time re-deriving that gap.
- Roughly **60–70% of the BRD** is achievable in the timeframe at deliverable quality. The excluded 30–40% is listed in `docs/scope-decisions.md` and must be excluded *in writing before code starts*.
- The client contact who last edited the BRD is **HADDAD Ramy** (AXA Middle East). Document created 2026-08-05.
- **No client answers have been received yet.** Every "TBC" in the docs is genuinely unknown — do not invent values for insurance types, email routing, or NEXT3 field names.
