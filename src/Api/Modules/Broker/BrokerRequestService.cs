using Api.Infrastructure;
using Api.Integrations.Email;
using Api.Modules.Audit;
using Api.Modules.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Api.Modules.Broker;

/// <summary>
/// Either the request an operation produced, or a refusal with the code the client branches on —
/// <c>DeclarationOutcome</c>'s shape, so the status mapping lives with the rule that decided it.
/// </summary>
/// <param name="EmailFailed">
/// True when the state committed but the send did not (§5.3's ordering). Not a refusal: the request
/// *is* submitted, and B1 says so while offering Resend. Lying about it — or rolling the submission
/// back — are the two worse options.
/// </param>
public sealed record BrokerOutcome(
    int StatusCode, string? ErrorCode, BrokerRequest? Request, bool EmailFailed = false)
{
    public static BrokerOutcome NotFound() => new(StatusCodes.Status404NotFound, null, null);

    public static BrokerOutcome Refused(int statusCode, string errorCode) =>
        new(statusCode, errorCode, null);

    public static BrokerOutcome Ok(BrokerRequest request, bool emailFailed = false) =>
        new(StatusCodes.Status200OK, null, request, emailFailed);
}

/// <summary>
/// design.md §5.3's Option 1, with the transactions and side effects each step owns (slice 5.2).
///
/// **This module never touches NEXT3** — no <c>INext3Client</c>, no outbox row, which is why
/// architecture rules 3 and 4 stay green without anything being added to them. Its terminal act is an
/// email.
/// </summary>
public sealed partial class BrokerRequestService(
    AppDbContext db,
    IEmailSender email,
    BrokerRequestEmail compose,
    AuditWriter audit,
    IOptionsMonitor<BrokerOptions> options,
    TimeProvider time,
    ILogger<BrokerRequestService> logger)
{
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Broker request {RequestId} is submitted, but the routed email to {Recipient} failed.")]
    private static partial void LogEmailFailed(
        ILogger logger, Guid requestId, string recipient, Exception exception);

    /// <summary>
    /// B2's create (§5.3). An Option 1 draft carrying all six fields — there is no edit endpoint, so
    /// they arrive together or not at all, and a mistake means a new request (the rule pass 3 already
    /// applies to B4: "a wrong submission means a new link").
    /// </summary>
    public async Task<BrokerOutcome> Create(
        Guid brokerUserId, string? displayName, BrokerRequestFields fields, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(fields);

        if (Validate(fields) is { } refusal)
        {
            return refusal;
        }

        var request = BrokerRequest.NewDraft(brokerUserId, displayName, time.GetUtcNow().UtcDateTime);
        request.InsuredName = fields.InsuredName;
        request.InsuranceType = fields.InsuranceType;
        request.InsuredAddress = fields.InsuredAddress;
        request.CarValue = fields.CarValue;
        request.EstimatedPremium = fields.EstimatedPremium;
        request.EffectiveDate = fields.EffectiveDate;

        db.BrokerRequests.Add(request);
        audit.Append(
            brokerUserId, AuditActions.BrokerRequestCreated, AuditEntityKinds.BrokerRequest, request.Id,
            new { request.Option, request.InsuranceType });

        await db.SaveChangesAsync(ct);
        return BrokerOutcome.Ok(request);
    }

    /// <summary>
    /// B2's submit (§5.3): commit the transition, **then** send.
    ///
    /// The order is deliberate and is recorded in `scope-decisions.md`. A send inside the transaction
    /// would roll a filed request back because a mail server was unreachable — work the broker has to
    /// do again, with the customer still sitting there. Committing first means the worst case is a
    /// request that is submitted with <c>emailed_at</c> null, which B1 shows as "email not yet sent"
    /// and offers Resend for. Same argument §5.2 makes for the officer fan-out.
    /// </summary>
    public async Task<BrokerOutcome> Submit(Guid id, Guid brokerUserId, CancellationToken ct)
    {
        var request = await Owned(id, brokerUserId, ct);
        if (request is null)
        {
            return BrokerOutcome.NotFound();
        }

        if (request.State != BrokerRequestState.Draft)
        {
            return BrokerOutcome.Refused(StatusCodes.Status409Conflict, "illegal_transition");
        }

        // Re-checked at submit as well as at create: the type list is configuration and may have
        // changed since the draft was written, and the recipient is resolved from the same table.
        if (Validate(Fields(request)) is { } invalid)
        {
            return invalid;
        }

        if (Recipient(request.InsuranceType) is not { } recipient)
        {
            return BrokerOutcome.Refused(StatusCodes.Status400BadRequest, "unknown_insurance_type");
        }

        // Before the transition, so a request whose bytes are gone is refused rather than left
        // submitted-and-unsendable.
        var (attachments, refusal) = await compose.Attachments(id, ct);
        if (refusal is not null)
        {
            return BrokerOutcome.Refused(refusal.StatusCode, refusal.ErrorCode);
        }

        request.Submit(time.GetUtcNow().UtcDateTime);
        audit.Append(
            brokerUserId, AuditActions.BrokerRequestSubmitted, AuditEntityKinds.BrokerRequest, id,
            new { request.InsuranceType, Documents = attachments.Count });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // `state` is the concurrency token, so somebody else submitted this request between our
            // read and our write. Nothing has been sent yet — the send is deliberately after this
            // commit — so the loser simply answers the same 409 an out-of-order request gets.
            db.ChangeTracker.Clear();
            return BrokerOutcome.Refused(StatusCodes.Status409Conflict, "illegal_transition");
        }

        var sent = await TrySend(request, recipient, attachments, brokerUserId, ct);
        return BrokerOutcome.Ok(request, emailFailed: !sent);
    }

    /// <summary>
    /// B4's Send email (§5.3) — Option 2's terminal act, and the mirror image of <see cref="Submit"/>.
    ///
    /// A separate path rather than a widened <see cref="Submit"/> or <see cref="Resend"/>, because the
    /// two options genuinely differ in who filled the form in: Option 1 goes `draft -> submitted` on
    /// the broker's own six fields, Option 2 goes `ready_to_send -> sent` on the customer's, after the
    /// public surface has already locked the token. Everything downstream of the transition is shared
    /// — the routing table, the attachment composition, `TrySend`'s claim on `emailed_at` — so the
    /// only thing this method owns is which edge it walks.
    ///
    /// **The attachments come out the same query as Option 1's**, which is why the customer's
    /// documents need no special handling: `BrokerRequestEmail.Attachments` selects on the owner alone,
    /// and `public_document` shares `broker_request` as its owner kind precisely so that it does.
    /// </summary>
    public async Task<BrokerOutcome> Send(Guid id, Guid brokerUserId, CancellationToken ct)
    {
        var request = await Owned(id, brokerUserId, ct);
        if (request is null)
        {
            return BrokerOutcome.NotFound();
        }

        // `Option != 2` as well as the state, for `Resend`'s reason in reverse: nothing else can reach
        // `ready_to_send`, but a guard that says what it means survives the next state being added.
        if (request.Option != 2 || request.State != BrokerRequestState.ReadyToSend)
        {
            return BrokerOutcome.Refused(StatusCodes.Status409Conflict, "illegal_transition");
        }

        // Re-validated here as well as at the public submit, for the reason `Submit` re-validates a
        // draft: #14's list is configuration and may have changed since the customer filled the form
        // in, and the recipient is resolved from the table keyed on it.
        if (Validate(Fields(request)) is { } invalid)
        {
            return invalid;
        }

        if (Recipient(request.InsuranceType) is not { } recipient)
        {
            return BrokerOutcome.Refused(StatusCodes.Status400BadRequest, "unknown_insurance_type");
        }

        // Before the transition, so a request whose bytes are gone is refused rather than left sent
        // and unsendable — and here that matters more than on Option 1, because these bytes are a
        // member of the public's identity documents and there is no second copy anywhere.
        var (attachments, refusal) = await compose.Attachments(id, ct);
        if (refusal is not null)
        {
            return BrokerOutcome.Refused(refusal.StatusCode, refusal.ErrorCode);
        }

        request.Send();
        audit.Append(
            brokerUserId, AuditActions.BrokerRequestSent, AuditEntityKinds.BrokerRequest, id,
            new { request.InsuranceType, Documents = attachments.Count });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // `state` is the concurrency token, so another press of Send email won this race. Nothing
            // has been sent yet — the send is deliberately after this commit — so the loser answers
            // the same 409 an out-of-order request gets, and AXA's desk sees one email rather than
            // four. The four-parallel-presses test is what holds this.
            db.ChangeTracker.Clear();
            return BrokerOutcome.Refused(StatusCodes.Status409Conflict, "illegal_transition");
        }

        var sent = await TrySend(request, recipient, attachments, brokerUserId, ct);
        return BrokerOutcome.Ok(request, emailFailed: !sent);
    }

    /// <summary>
    /// B1's Resend: **sends only**. No transition and no second `submitted_at`; the once-only guard
    /// is <see cref="TrySend"/>'s claim on <c>emailed_at</c>, not the check below, which is only the
    /// early exit that gives a tidy 409. (The artboard's "no resend" is about a request that *did*
    /// send — a delivered request is refused here, and that case is not this one.)
    ///
    /// **Both options since slice 5.3**: Option 1 in `submitted` and Option 2 in `sent`, each only
    /// while `emailed_at` is null. See the guard below for why widening it does not reopen the hole
    /// 5.2 narrowed it to close.
    /// </summary>
    public async Task<BrokerOutcome> Resend(Guid id, Guid brokerUserId, CancellationToken ct)
    {
        var request = await Owned(id, brokerUserId, ct);
        if (request is null)
        {
            return BrokerOutcome.NotFound();
        }

        // **Widened in slice 5.3, and the original argument is untouched.** 5.2 wrote this as
        // `Option 1 && Submitted` to stop a broker mailing a customer's submission to AXA straight
        // past §5.3's B4 review — `emailed_at` set while the state still said `ready_to_send`, which
        // would also have started the retention clock on documents B4 had not sent yet. That is
        // exactly why `ready_to_send` is still refused below.
        //
        // `sent` is the different case. It means B4's review has happened and `Send` has committed,
        // and the only way to be *in* it with `emailed_at` null is a send that failed after the
        // transition — §5.3's commit-then-send ordering, the same visible failure Option 1 leaves
        // behind. Without this arm, a mail server being briefly down would strand a member of the
        // public's completed submission with no way to deliver it and no way to redo it: the customer
        // is gone, their link is locked, and the broker would have to ask them to start again.
        // "No resend" on the BrokerStates artboard is about a request that genuinely *did* send, and
        // the `EmailedAt` check immediately below is what enforces that reading.
        var resendable = request.State switch
        {
            BrokerRequestState.Submitted => request.Option == 1,
            BrokerRequestState.Sent => request.Option == 2,
            _ => false,
        };

        if (!resendable)
        {
            return BrokerOutcome.Refused(StatusCodes.Status409Conflict, "not_submitted");
        }

        if (request.EmailedAt is not null)
        {
            return BrokerOutcome.Refused(StatusCodes.Status409Conflict, "already_emailed");
        }

        if (Recipient(request.InsuranceType) is not { } recipient)
        {
            return BrokerOutcome.Refused(StatusCodes.Status400BadRequest, "unknown_insurance_type");
        }

        var (attachments, refusal) = await compose.Attachments(id, ct);
        if (refusal is not null)
        {
            return BrokerOutcome.Refused(refusal.StatusCode, refusal.ErrorCode);
        }

        var sent = await TrySend(request, recipient, attachments, brokerUserId, ct);
        return BrokerOutcome.Ok(request, emailFailed: !sent);
    }

    internal static BrokerRequestFields Fields(BrokerRequest request) =>
        new(request.InsuredName, request.InsuranceType, request.InsuredAddress,
            request.CarValue, request.EstimatedPremium, request.EffectiveDate);

    private Task<BrokerRequest?> Owned(Guid id, Guid brokerUserId, CancellationToken ct) =>
        db.BrokerRequests.SingleOrDefaultAsync(r => r.Id == id && r.BrokerUserId == brokerUserId, ct);

    private string? Recipient(string? insuranceType) =>
        insuranceType is not null
        && options.CurrentValue.EmailRouting.TryGetValue(insuranceType, out var recipient)
            ? recipient
            : null;

    /// <summary>
    /// The send, and the only place <c>emailed_at</c> is written. Returns false rather than throwing:
    /// the caller has already committed and the failure is a visible state, not an error.
    /// </summary>
    private async Task<bool> TrySend(
        BrokerRequest request,
        string recipient,
        IReadOnlyList<EmailAttachment> attachments,
        Guid brokerUserId,
        CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;

        // **The claim is the guard, and it is in the `WHERE`.** `state` cannot arbitrate this one:
        // a Resend changes no state, so two simultaneous presses would carry the same token value and
        // both would match — the read-then-write CLAUDE.md names first among the recurring bug classes,
        // and AXA's desk receiving the same quotation twice is what it costs here. Claiming
        // `emailed_at` first means the second caller updates no row and sends nothing.
        var claimed = await db.BrokerRequests
            .Where(r => r.Id == request.Id && r.EmailedAt == null)
            .ExecuteUpdateAsync(
                s => s.SetProperty(r => r.EmailedAt, now).SetProperty(r => r.EmailRecipient, recipient),
                ct);

        if (claimed == 0)
        {
            return false;
        }

        // Keeps the tracked copy in step with the row, so the endpoint's answer names the recipient.
        request.MarkEmailed(recipient, now);

        try
        {
            await email.Send(
                recipient,
                compose.Subject(request),
                compose.Body(request),
                NotificationTemplates.BrokerRequestSubmitted,
                // Null: the recipient is an AXA mailbox from the routing table, not an app user.
                recipientUserId: null,
                attachments,
                ct);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            // The sender has already written its own `failed` notification row (§8), which is what
            // B1 reads. Broad by the AssignmentHandler rule: the fake throws FakeTransientException
            // and an HTTP client reports its own timeout as TaskCanceledException, and neither may
            // fault a transition that has already committed.
            LogEmailFailed(logger, request.Id, recipient, failure);

            // Release the claim, or the request reads as delivered for ever and B1 never offers the
            // Resend that is the whole point of committing the state first. The compensating write is
            // unconditional: this caller is the one holding the claim.
            await db.BrokerRequests
                .Where(r => r.Id == request.Id)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(r => r.EmailedAt, (DateTime?)null)
                          .SetProperty(r => r.EmailRecipient, (string?)null),
                    ct);

            db.ChangeTracker.Clear();
            return false;
        }

        audit.Append(
            brokerUserId, AuditActions.BrokerRequestEmailed, AuditEntityKinds.BrokerRequest, request.Id,
            new { Recipient = recipient, Attachments = attachments.Count });

        await db.SaveChangesAsync(ct);
        return true;
    }

    private BrokerOutcome? Validate(BrokerRequestFields fields)
    {
        if (string.IsNullOrWhiteSpace(fields.InsuredName)
            || string.IsNullOrWhiteSpace(fields.InsuredAddress)
            || fields.EffectiveDate is null)
        {
            return BrokerOutcome.Refused(StatusCodes.Status400BadRequest, "incomplete_request");
        }

        if (fields.InsuranceType is null
            || !options.CurrentValue.InsuranceTypes.Contains(fields.InsuranceType, StringComparer.Ordinal))
        {
            return BrokerOutcome.Refused(StatusCodes.Status400BadRequest, "unknown_insurance_type");
        }

        // Zero is refused as well as negative: a quotation for a car worth nothing, or a premium of
        // nothing, is a form somebody tabbed through rather than filled in.
        return fields.CarValue is not > 0 || fields.EstimatedPremium is not > 0
            ? BrokerOutcome.Refused(StatusCodes.Status400BadRequest, "invalid_amount")
            : null;
    }
}

/// <summary>§5.3's six form fields, as one value so create and submit validate the same thing.</summary>
public sealed record BrokerRequestFields(
    string? InsuredName,
    string? InsuranceType,
    string? InsuredAddress,
    decimal? CarValue,
    decimal? EstimatedPremium,
    DateOnly? EffectiveDate);
