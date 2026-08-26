namespace Api.Tests.Integrations;

/// <summary>
/// A fact that runs only when a real FCM service-account key is configured, and reports itself
/// skipped when it is not.
///
/// The third use of this shape, after <c>AzuriteFactAttribute</c> and <c>SandboxFactAttribute</c>,
/// and for the same reason: `Push:Fcm:Enabled` is false everywhere and Appendix A's values are
/// placeholders, so the suite must stay green on a machine with no Firebase project — but then the
/// one part of the FCM adapter that no stub can exercise has no test at all.
///
/// **What only this can prove.** <c>FcmPushSenderTests</c> scripts Google's responses, so it says
/// nothing about whether the assertion this application signs is one Google will actually accept:
/// the PEM import, the RS256 signature, the claim set, the clock, and the key itself. That gap is
/// not hypothetical — the disposed-`RSA` bug this adapter shipped with would have passed every
/// stubbed test and then failed in production about an hour after each deploy.
///
/// Configured by environment variable rather than by user-secrets on purpose: <c>ApiFixture</c> runs
/// the suite as `Production` precisely so it cannot read whatever the developer happens to have in
/// their secret store, and reaching around that here would undo it. CLAUDE.md carries the two
/// variables.
/// </summary>
public sealed class FcmLiveFactAttribute : FactAttribute
{
    /// <summary>Path to a service-account JSON key with FCM send rights.</summary>
    public const string KeyPathVariable = "FCM_LIVE_SERVICE_ACCOUNT_JSON";

    /// <summary>The Firebase project that key belongs to.</summary>
    public const string ProjectVariable = "FCM_LIVE_PROJECT_ID";

    public FcmLiveFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(ProjectId))
        {
            Skip = $"{ProjectVariable} is not set (see CLAUDE.md).";
            return;
        }

        if (string.IsNullOrWhiteSpace(KeyPath))
        {
            Skip = $"{KeyPathVariable} is not set (see CLAUDE.md).";
            return;
        }

        if (!File.Exists(KeyPath))
        {
            // Named separately from "not set", because a path that points at nothing is a mounted
            // secret that did not arrive rather than a machine that was never meant to run this.
            Skip = $"{KeyPathVariable} points at no file ('{KeyPath}').";
        }
    }

    public static string? KeyPath => Environment.GetEnvironmentVariable(KeyPathVariable);

    public static string? ProjectId => Environment.GetEnvironmentVariable(ProjectVariable);
}
