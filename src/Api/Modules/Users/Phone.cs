using System.Text.RegularExpressions;

namespace Api.Modules.Users;

/// <summary>E.164 validation — one regex for the OTP request and admin create paths.</summary>
public static partial class Phone
{
    public static bool IsValidE164(string phone) => E164().IsMatch(phone);

    [GeneratedRegex(@"^\+[1-9]\d{7,14}$")]
    private static partial Regex E164();
}
