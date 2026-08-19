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

    // §9's "public-page submissions (actor null, token id logged)".
    public const string PublicLinkIssued = "public_link_issued";
    public const string PublicLinkOpened = "public_link_opened";
    public const string PublicLinkSubmitted = "public_link_submitted";
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
    public const string PublicLinkToken = "public_link_token";
}
