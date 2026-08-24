# Scope letter — Motor Claim Management application

**To:** HADDAD Ramy, AXA Middle East
**From:** Ali Sleiman
**Status:** DRAFT — not sent. Drafted slice 4.3 (2026-08-22); still unsent when the demo ran 2026-08-24 — gate breach recorded in the build playbook week-4 checklist.
**Purpose:** written acknowledgement of scope, exclusions and dependencies. Drafted to precede the week-4 demo; the demo ran 2026-08-24 before it was sent, and nothing shown there changes the scope stated below.
Email acknowledgement is sufficient; a signature is not required.

Deliberate omissions, so they are not added back without a reason:
- **No WAF, no CAPTCHA, no Key Vault.** All three are optional unless AXA Group security mandates
  them. The InfoSec question below surfaces the requirement if it exists; there is no need to hand
  over a $330/month idea unprompted.
- **No re-litigation of price or timeline.** This letter fixes *what* is being built. The date moves
  only through the dependency clause, and it says so once, plainly.
- **No apology for what is excluded.** Each exclusion is stated as a decision with a reason.

---

**Subject: Motor Claim App — scope confirmation before the week-4 demonstration**

Dear Ramy,

Ahead of the demonstration this week, here is what is being built, what is not, and what I need from
AXA. Please reply confirming it reads correctly. If anything below is wrong, it is much cheaper to
correct now than after it is built.

## 1. What is included

**Administration and onboarding.** Creation and management of the four user types — expert, garage,
claim officer, broker — with invitation by SMS, login by phone and one-time code, and activation or
deactivation. Each profile carries its NEXT3 identifier.

**The expert module, complete.** A claim assigned in NEXT3 raises a notification on the expert's
phone. The expert opens the claim, confirms arrival — which writes the date, time and GPS position
back to NEXT3 — and captures photographs into the four buckets the BRD defines, plus a voice note, a
marked-up damage diagram, and the expert report. Every image passes a quality check before it is
sent, and everything reaches NEXT3 under the correct visa number.

**The garage and claim-officer survey flow, complete.** A garage files a declaration with
photographs and documents; a claim officer is notified, reviews it, searches NEXT3 for the visa,
and approves or rejects it with comments. On approval the decision is rendered as an image and,
together with the garage's documents, is filed into the NEXT3 *Survey* folder under that visa, and
the garage is notified. Post-repair uploads follow the same path.

**The broker module, both options.** Option 1 is the broker's own form and document upload, emailed
to the AXA recipient for that insurance type. **Option 2 — the broker sends a link to the customer,
who completes the form, uploads their documents and photographs all four sides and the roof of the
car, after which the file returns to the broker as *ready to send* — is included.** It was not in my
original estimate. I have added it because the BRD describes it and a delivery without it would not
match what you asked for. **It is the single largest addition to the work, and it is being absorbed
without changing the price or the date** — see section 3 for what that cost.

**NEXT3 integration** for all of the above, through the interface specification I have sent you
separately (`next3-openapi.yaml`).

## 2. What is excluded

These are excluded deliberately. Each one is available as separately quoted work.

| Excluded | Why |
|---|---|
| **Offline mode** | Not in the BRD. I want to flag it honestly: an expert at a roadside accident may have no signal, and the application requires one. I recommend it as a phase 2. |
| **Arabic / RTL and French** | English only. Right-to-left layout is not a translation exercise; it is a second layout. |
| **Production SLA and 24/7 support** | Replaced by a defined hypercare window after go-live. |
| **Reporting, MIS and dashboards** | Not requested in the BRD. |
| **Voice transcription** | Voice notes are recorded, uploaded and stored. They are not transcribed. |
| **Creating a visa from inside the application** | Where a visa does not exist, the claim officer creates it in NEXT3 and returns. Confirm this matches your process. |
| **Damage-diagram refinement** | The diagram is functional — a car outline, tap to mark the damage, exported as an image. It is not a polished graphic. |
| **A second round of user acceptance testing** | One round, defined below. |

## 3. What Option 2 cost, stated plainly

Adding Option 2 to an eight-week plan required removing something. Rather than move the date, I
removed the damage-diagram refinement and the second UAT round, and reused the expert's photo
capture and quality-check machinery for the customer-facing page.

**There is nothing further to remove.** Any delay to the dependencies in section 5 moves the
delivery date by the same number of days. I would rather say that now than report it in week seven.

## 4. Three decisions you should see before the demonstration

**A rejected declaration shows the garage the decision, but not the reason.** The BRD grants comment
visibility only on confirmation, so I have built it that way. I expect this to surprise your users.
If you want the rejection reason shown to the garage, tell me — it is a small change, and much
smaller now than later.

**Rejection is final.** A rejected declaration cannot be edited and resubmitted; the garage files a
new one. This follows the BRD's *disregard case*, which defines no resubmission path.

**Duplicate documents on retry — the one risk I cannot engineer away.** When the application sends a
document to NEXT3 and the connection times out, nothing on my side can know whether NEXT3 accepted
it. Retrying may file it twice; not retrying may lose it, and losing it is worse. Every push
therefore carries a unique reference, and **the specification asks NEXT3 to ignore a repeat of a
reference it has already accepted.** If NEXT3 implements that, retries are safe. **If it does not,
occasional duplicate documents under a visa are unavoidable, and no amount of work on my side
prevents them.** This is the one place where a decision by the NEXT3 team changes the quality of the
result, so I want it acknowledged rather than discovered.

## 5. What I need from AXA, and by when

| # | Needed | By | Consequence if late |
|---|---|---|---|
| 1 | **NEXT3 sandbox URL, credentials and authentication method** | Immediately — it was due in week 3 | **The delivery date moves day for day from the demonstration onwards.** Everything you will see runs against a simulator. |
| 2 | Confirmation of who builds the NEXT3 endpoints, and their schedule | Immediately | Same as above; this is the largest risk in the project |
| 3 | Whether an AXA Group InfoSec review or penetration test is required | Immediately | If required and performed on my schedule, it will not fit inside two months |
| 4 | Document-type codes and folder names for NEXT3 | Week 5 | Documents file against placeholder codes and must be re-pushed |
| 5 | The insurance-type list and the email recipient for each | Week 5 | The broker module cannot send to a real address |
| 6 | SMS gateway and account for one-time codes | Week 5 | Login works in demonstration only |
| 7 | Azure subscription access, and accounts in AXA's name | Week 5 | No deployment target |

## 6. Acceptance

One round of user acceptance testing, with named participants, a written test list agreed
beforehand, and a fixed window for corrections. Defects found in that round are fixed within it. New
requirements found in that round are quoted separately — not because they are unwelcome, but because
an unbounded acceptance round has no end and this engagement has a fixed price.

Please confirm by reply. If I have misread anything, say so and I will correct it before the
demonstration.

Best regards,
Ali Sleiman
