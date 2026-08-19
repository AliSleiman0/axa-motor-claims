# Client email — NEXT3 integration + running costs

**To:** HADDAD Ramy, AXA Middle East
**Status:** draft, not sent
**Date drafted:** 2026-08-18

Deliberate omissions (do not add back without a reason):
- **WAF not mentioned.** It is optional unless AXA Group security mandates it. The InfoSec/pen-test
  question below will surface the requirement if it exists — no need to hand them a $330/month idea.
- **Cloud macOS build service not mentioned.** Codemagic gives 500 free macOS M2 minutes/month on a
  personal account (verified 2026-08-18), which covers this project. Developer also has an iPhone 17
  Pro Max for real-device testing, and may acquire a Mac. Zero cost to AXA either way.
- **No SMS cost figure.** Rate is entirely provider-dependent, so it is asked as a question, not quoted.

---

**Subject: Motor Claim App — NEXT3 integration questions and running costs**

Dear Ramy,

Thank you again for the BRD. I've completed the technical analysis and the architecture is decided, so I can start building immediately. Before I do, there are two things I need from your side.

**1. NEXT3 integration — the critical path**

The application's core function is pushing photos into NEXT3 under the correct visa number, so NEXT3 access determines the delivery date more than any other factor. I can build the first three weeks against a simulated NEXT3 interface, but from week 3 onward I need the real thing.

To be direct about the schedule: **every day the NEXT3 sandbox credentials are delayed beyond week 3, the delivery date moves by the same day.** I'd rather flag that now than report it later.

The four questions that matter most:

1. Who will build the NEXT3-side endpoints — your team or the NEXT3 vendor? Is that work budgeted and scheduled, and by when?
2. On what date can I receive a sandbox URL, credentials, and the authentication method (OAuth / API key / mTLS)?
3. Are the endpoints reachable from the internet, or internal-only? If internal, who configures connectivity? My recommendation is an outbound tunnel installed on your side — it needs no inbound firewall rules and is the fastest to approve.
4. Is the integration a proper API, or a shared directory plus database inserts? The BRD prerequisites mention identifying a directory and inserting records, which suggests the latter — either works, but they are different builds.

A further seven questions (document type codes, duplicate handling, how the app is notified of a new assignment, and others) are in the attached list. They're needed before week 3 but aren't blocking today.

To speed this up, I will send you a **proposed API specification** this week — roughly eight endpoints, defined in a standard format. Rather than waiting for a specification from NEXT3, your team or vendor can review mine, amend it, and build to it. It removes a round of guessing on both sides.

**2. Running costs and accounts**

The hosting cost is modest, but the accounts need to be in AXA's name from the start.

- Azure hosting and monitoring: **approximately $70–95 per month**
- SMS for login one-time-passwords: **depends on which provider you choose** — see below

For context, at 3,000 claims per month the total lands **under $0.05 per claim**, against an estimated $0.50 per claim for the current manual email handling.

**The SMS provider is a decision I need from you.** Login uses a one-time password sent by SMS, so the app needs a messaging account. Per-message rates vary widely by provider and by country, and Lebanon in particular is priced very differently across them. Three realistic options:

- **Monty Mobile** or a similar regional aggregator — usually the best rates for Lebanon and the Gulf, and a regional provider AXA may already have a relationship with
- **Twilio** — most expensive per message, but the fastest to set up and the best documented
- **Azure Communication Services** — keeps everything on one Azure bill, but I need to confirm it covers your target countries before recommending it

If AXA already holds an SMS gateway contract for other systems, that is almost certainly the cheapest answer and I will integrate against it. Otherwise, tell me which of the above you prefer and I'll confirm the exact monthly cost against your expected user numbers. Either way the account should be in AXA's name.

I'd ask the same of all third-party accounts — Azure, SMS gateway, domain, and any mobile developer accounts. It avoids reimbursement administration on both sides and means you own the infrastructure from day one, which you'll want at handover regardless.

One question that sits outside my control and can affect the timeline: **will AXA Group InfoSec review this application, and is a penetration test required?** If so, I need to know now, because the review schedule is not something I can compress, and it is the single most likely thing to push us past two months. If your security baseline imposes specific infrastructure requirements, please send them with the answer so I can build to them rather than retrofit.

**3. Mobile devices**

Three quick ones that affect how the app is delivered:

- Roughly what share of your expert and garage network uses Android versus iPhone?
- Should the app be distributed through the public app stores, or internally via your device management? Internal distribution removes store review cycles entirely.
- Does AXA already hold an Apple Developer account?

If the network is predominantly Android, the delivery becomes noticeably simpler and faster.

I'd be grateful for answers to the four NEXT3 questions, the SMS provider choice, and the security question within five working days, as they determine the sequence of the first three weeks. Happy to walk through any of this on a call.

Best regards,
Ali Sleiman

---

## Attachment — NEXT3 questions to forward to the NEXT3 owner

Blocking (needed to start week 3):

1. Sandbox URL, credentials, authentication method, rate limits, and test data — and the date these can be provided.
2. Are the endpoints internet-reachable or internal-only? If internal, who configures the connectivity?
3. Who builds the endpoints — AXA team or NEXT3 vendor? Is the work budgeted and scheduled?
4. Is document upload an API endpoint, or a shared directory plus a database insert? If the latter, which database engine does NEXT3 run on? What is the maximum file size?

Needed before week 3:

5. Does NEXT3 deduplicate on a client-supplied reference ID? Required so that a retry after a network timeout cannot create a duplicate photo in NEXT3.
6. How does the app learn that a new visa has been assigned to an expert? The BRD refers to a back-office engine triggering the process but does not say how that reaches the application — a webhook from NEXT3 is preferred, otherwise the app must poll.
7. The exact document-type codes for the "Expert documents" folder and the "Survey" folder.
8. The writable fields for the "Expert Arrived" update — field names, endpoint, and the expected date, time, and location formats.
9. NEXT3's expected availability and any scheduled maintenance windows, so retry behaviour can be sized correctly.
10. The expert extract (NEXT3 ID, name, phone) — in what format, and is it one-time or synchronised?
11. Does NEXT3 accept audio files, and under which document type? (The BRD requires expert voice notes.)
12. Is there an AXA-standard car damage diagram or set of damage codes, or is free-form marking acceptable?
