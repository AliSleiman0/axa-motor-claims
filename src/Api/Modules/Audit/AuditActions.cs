namespace Api.Modules.Audit;

/// <summary>The §9 audit event names wired so far — the single source for action strings.</summary>
public static class AuditActions
{
    public const string LoginSucceeded = "login_succeeded";
    public const string LoginFailed = "login_failed";
    public const string UserActivated = "user_activated";
    public const string ProfileCreated = "profile_created";
    public const string ProfileUpdated = "profile_updated";
    public const string UserDeactivated = "user_deactivated";
    public const string InviteIssued = "invite_issued";

    // §5.1's expert flow. assignment_received and assignment_unmapped_expert have no actor: NEXT3
    // sent them, not a user.
    public const string AssignmentReceived = "assignment_received";
    public const string AssignmentUnmappedExpert = "assignment_unmapped_expert";
    public const string AssignmentOpened = "assignment_opened";

    // §9: "Arrived presses with coordinates" — the lat/lng go in the detail JSON, because the whole
    // point of the event is where the expert said they were.
    public const string AssignmentArrived = "assignment_arrived";

    // §5.2's declaration machine. §9 requires "every declaration transition with actor" and
    // "approval/rejection with comments hash" — a claims dispute turns on who decided what and when,
    // and the declaration row only keeps the *latest* state, so the trail is where the history lives.
    public const string DeclarationCreated = "declaration_created";
    public const string DeclarationSubmitted = "declaration_submitted";
    public const string DeclarationApproved = "declaration_approved";
    public const string DeclarationRejected = "declaration_rejected";
    public const string DeclarationRepairsStarted = "declaration_repairs_started";
    public const string DeclarationRepairDocsSubmitted = "declaration_repair_docs_submitted";

    // §9's "public-page submissions (actor null, token id logged)".
    // §5.3's broker requests (slice 5.2). `Emailed` is separate from `Submitted` because the state
    // commits before the send: a request can be submitted and not yet emailed, and a Resend emails
    // one without transitioning it, so one action could not honestly cover both.
    public const string BrokerRequestCreated = "broker_request_created";
    public const string BrokerRequestSubmitted = "broker_request_submitted";
    public const string BrokerRequestEmailed = "broker_request_emailed";

    // Slice 5.3: Option 2's terminal transition. Distinct from `BrokerRequestSubmitted` because the
    // two options are submitted by different people — the broker in Option 1, the customer in Option 2
    // (whose act is already logged as `public_link_submitted` with a null actor). This row is the
    // broker's decision to release that submission to AXA, and `BrokerRequestEmailed` beside it is
    // whether the mail actually left.
    public const string BrokerRequestSent = "broker_request_sent";

    public const string PublicLinkIssued = "public_link_issued";
    public const string PublicLinkOpened = "public_link_opened";
    public const string PublicLinkSubmitted = "public_link_submitted";

    // §9: "every media upload (who, which claim/declaration/request, when, origin flag)". This is the
    // InfoSec answer to "who uploaded which photo" — the one audit event the BRD's claims-dispute
    // scenario actually turns on. document_blob_deleted records §7.3's retention sweep, so a photo
    // that is no longer in the transit container can still be accounted for.
    public const string DocumentUploaded = "document_uploaded";
    public const string DocumentBlobDeleted = "document_blob_deleted";

    // §9: "outbox retries from A2". An admin reaching into the queue and re-sending a push AXA has
    // not received is an intervention in the one pipeline this whole project exists to make
    // reliable, so it is recorded with who did it. Two actions rather than one because they answer
    // different questions: the singular carries the message id, the plural carries a count and no
    // entity, and collapsing them would make "somebody retried everything at 09:14" indistinguishable
    // from thirty separate decisions.
    public const string OutboxPushRetried = "outbox_push_retried";
    public const string OutboxPushesRetried = "outbox_pushes_retried";

    // ---- slice 7.2: the device registry (§8's tables, §9's trail) ----
    //
    // Registering, losing and taking over a device are all decisions about **who receives a claim
    // popup carrying a visa number**, and until this slice none of them left a trace. The three fire
    // on creation, displacement and revocation only — never on the refresh path, because the
    // Capacitor shell re-registers its FCM token on every launch and auditing that would bury the
    // events that matter under one row per app open, per handset, per day.
    //
    // device_token_displaced is the one worth the extra action rather than a Reason on the revoke:
    // it is a handset changing hands. FCM's token identifies the *app install*, not the person, so
    // signing out deletes nothing and a pooled field phone would otherwise keep delivering the
    // previous holder's claims. The actor is the **new** registrant and the entity is the row that
    // was taken away, which is the only shape that answers "why did A stop getting notifications".
    public const string DeviceTokenRegistered = "device_token_registered";
    public const string DeviceTokenRevoked = "device_token_revoked";
    public const string DeviceTokenDisplaced = "device_token_displaced";
    public const string PushSubscriptionRegistered = "push_subscription_registered";
    public const string PushSubscriptionRemoved = "push_subscription_removed";

    // §4's refresh-token rule: presenting an already-rotated token revokes the whole family. That is
    // the app's only automatic "somebody may be replaying a stolen credential" signal, and it was
    // silent — the user simply found themselves signed out. Actor is the token's owner, since the
    // presenter is by definition unauthenticated.
    public const string RefreshTokenFamilyRevoked = "refresh_token_family_revoked";

    // §5.3's send failure. The compensating write that releases `emailed_at` runs outside the change
    // tracker and logs, so the only durable record was a `notification` row saying the send failed —
    // not that the claim was taken and given back. B1's Resend button exists because of this state;
    // the trail should say how the request got into it.
    public const string BrokerEmailSendFailed = "broker_email_send_failed";
}

/// <summary>Entity-kind strings for <see cref="AuditLog.EntityKind"/>.</summary>
public static class AuditEntityKinds
{
    public const string AppUser = "app_user";
    public const string ExpertProfile = "expert_profile";
    public const string GarageProfile = "garage_profile";
    public const string ClaimOfficerProfile = "claim_officer_profile";
    public const string BrokerProfile = "broker_profile";
    public const string BrokerRequest = "broker_request";
    public const string ExpertAssignment = "expert_assignment";
    public const string PublicLinkToken = "public_link_token";
    public const string Document = "document";
    public const string Declaration = "declaration";

    /// <summary>
    /// §5.4's A2 retries. The audit row names the table rather than the entity type, like every
    /// other value here — and deliberately so: `Api.Modules.Audit` may not reference
    /// `Next3OutboxMessage` (architecture rule 4), and a string is all it ever needs.
    /// </summary>
    public const string Next3Outbox = "next3_outbox";

    /// <summary>
    /// §8's two device tables (slice 7.2). Two kinds rather than one "device", because they are two
    /// tables with different lifecycles — a browser subscription is revoked when the push service
    /// says 404/410, an FCM token when the handset changes hands — and a support query that could
    /// not tell them apart would be answering the wrong half of "which device did we notify".
    /// </summary>
    public const string DeviceToken = "device_token";

    /// <inheritdoc cref="DeviceToken"/>
    public const string PushSubscription = "push_subscription";
}
