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

    // §9's "public-page submissions (actor null, token id logged)".
    public const string PublicLinkIssued = "public_link_issued";
    public const string PublicLinkOpened = "public_link_opened";
    public const string PublicLinkSubmitted = "public_link_submitted";

    // §9: "every media upload (who, which claim/declaration/request, when, origin flag)". This is the
    // InfoSec answer to "who uploaded which photo" — the one audit event the BRD's claims-dispute
    // scenario actually turns on. document_blob_deleted records §7.3's retention sweep, so a photo
    // that is no longer in the transit container can still be accounted for.
    public const string DocumentUploaded = "document_uploaded";
    public const string DocumentBlobDeleted = "document_blob_deleted";
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
}
