namespace Api.Modules.Notifications;

/// <summary>
/// Log of every push/SMS/email attempt (design.md §4, §8). Write-only from the app's point of view —
/// nothing reads it back into a flow; it exists for support ("did the OTP go out?") and for the
/// §9 audit story. CreatedAt is beyond §4's letter: a failed send never sets SentAt, so without it a
/// failed row has no timestamp at all (realized 2026-08-19, slice 1.4).
/// </summary>
public sealed class Notification
{
    public Guid Id { get; set; }
    public required string Channel { get; set; }
    public Guid? RecipientUserId { get; set; }
    public required string RecipientAddress { get; set; }
    public required string Template { get; set; }
    public string? Payload { get; set; }
    public required string Status { get; set; }
    public DateTime? SentAt { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>The §4 nvarchar enum values for `notification.channel`.</summary>
public static class NotificationChannels
{
    public const string Push = "push";
    public const string Sms = "sms";
    public const string Email = "email";
}

/// <summary>The §4 nvarchar enum values for `notification.status`.</summary>
public static class NotificationStatuses
{
    public const string Queued = "queued";
    public const string Sent = "sent";
    public const string Failed = "failed";
}

/// <summary>
/// Template names, so the same send always logs the same key. Not client data — these name our own
/// message templates, not AXA's content.
/// </summary>
public static class NotificationTemplates
{
    public const string OtpCode = "otp_code";
    public const string Invite = "invite";

    /// <summary>The BRD's primary trigger: "a popup message will show on the expert mobile" (§8).</summary>
    public const string AssignmentReceived = "assignment_received";
}
