# Client email — post-demo package: scope letter, week-4 status, NEXT3 spec

**To:** HADDAD Ramy, AXA Middle East
**From:** Ali Sleiman
**Status:** SENT to AXA (developer's report, 2026-08-26; exact date unconfirmed) — awaiting acknowledgement. Drafted 2026-08-24 (PO session), after the demo ran.
**Attachments (three):** `docs/scope-letter.md` · `docs/status-2026-08-22.md` · `docs/next3-openapi.yaml`

Why this email exists: the demo was given on 2026-08-24 with none of the client documents sent — the
gate breach recorded in the build playbook. This is the recovery: it puts the written scope in AXA's
hands before their post-demo impressions harden into assumptions, restates the two dated commitments
the status carries (sandbox gate, day-for-day clock), and folds in the three questions raised since
the 08-18 draft rather than opening a fourth thread.

Deliberate omissions (do not add back without a reason):
- **No apology for the demo preceding the letter.** The letter is presented as the written record of
  what was shown; drawing attention to the sequencing serves nobody.
- **The 30% milestone is mentioned in one sentence and not negotiated.** Payment terms already say
  30% at the week-4 demo; the invoice follows separately when Ali sends it. This email only marks
  the milestone as reached.
- **The 2026-08-18 email (NEXT3 integration + running costs) is NOT merged in.** It is still unsent
  and still needed — its 4 commercial questions + 12-question NEXT3 attachment stand alone and
  should go to the NEXT3 owner. Send it the same day as this one, as its own thread, or the sandbox
  questions drown in the scope acknowledgement.

---

Subject: **Motor Claims app — scope confirmation and three questions following Monday's demo**

Dear Mr. Haddad,

Thank you for your time at Monday's demonstration. Everything shown — the expert flow with live
notifications, the garage declaration and approval chain, and the queue draining after the simulated
NEXT3 outage — is running against the simulated NEXT3 described below, which is the main thing this
email is about.

**Attached are three documents; the first needs a reply.**

1. **Scope letter.** The written record of what is being built, what is excluded, and the
   dependencies the date rests on. A simple email acknowledgement is sufficient — no signature
   needed. Until acknowledged, the scope shown at the demo is only my understanding of it.
2. **Week-4 status.** Two dated commitments in it are worth stating here as well: the NEXT3 sandbox
   is still not available to me, and **from the demonstration date, each day the sandbox remains
   unavailable moves delivery day-for-day.** Everything demonstrated runs on the simulator; the swap
   to the real system is built and waiting on credentials.
3. **NEXT3 OpenAPI specification.** The exact endpoints the application needs NEXT3 to expose —
   ready to forward to whoever owns NEXT3 integration on your side.

**Four questions that have come up since:**

1. **Which phones do your experts and garages actually carry?** A rough Android / iPhone split is
   enough. On Android the app ships as a native app; on iPhone it installs from the browser. If your
   network is largely Android, the iOS app-store track (and the Apple developer account it needs)
   drops off the critical path entirely — this answer directly shortens the plan.
2. **Does NEXT3 care about document file names?** iPhones name every captured photo `image.jpg`, so
   several photos under one claim can share a name. Each document already carries a unique reference
   and a document-type code — please confirm NEXT3 keys on those, not on the file name.
3. **Which currency are car value and estimated premium in?** The BRD names the fields but not the
   currency. One currency for the whole deployment, or per insurance type?

4. **May a broker's request email go out with no documents attached?** The broker form promises
   the details *and* the attached documents, but the BRD sets no minimum. The customer-link flow
   already requires at least one document — please confirm whether the broker's own form should
   enforce the same rule.

Separately, the week-4 demonstration milestone from our payment schedule has now been reached; the
corresponding invoice will follow in its own email.

I would also welcome any reactions or change requests from your side following the demonstration —
anything raised will be assessed against the attached scope letter and answered with either "in
scope" or a short written variation, so nothing gets absorbed silently.

Best regards,
Ali Sleiman

---

**Send checklist (developer's act, not the PO's):** attach exactly the three files · send the
2026-08-18 NEXT3/costs email the same day as its own thread · on any reply, route change requests
through `docs/scope-decisions.md` before the backlog · record the send date here and flip this
banner and the three attachments' banners from DRAFT · then tick the week-4 checklist box with the
date and a note that the send followed the demo (the breach note stays).
