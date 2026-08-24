namespace Api.Tests.Integrations;

/// <summary>
/// Configuration a host needs before it will start at all, for the boot tests that build one
/// (slice 5.2).
///
/// It exists because <c>BrokerOptionsValidator</c> is the first validator registered
/// **unconditionally** — the `Next3` and `Push` ones run only in their live mode — so a host built
/// from a dictionary that names no `Broker` section now fails to start, correctly. The two existing
/// boot harnesses supply `ConnectionStrings` and `Auth:Jwt` for the same reason and in the same
/// spirit; this is the third such block, and having one copy of it is what stops the fourth
/// disagreeing with the first three.
/// </summary>
public static class BootConfiguration
{
    /// <summary>
    /// The `Broker` section of `appsettings.Placeholders.json`, as checked in — the same three types
    /// and three routes every environment runs on today.
    /// </summary>
    public static Dictionary<string, string?> WithBroker(this Dictionary<string, string?> settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings["Broker:AllowUpload"] = "true";
        settings["Broker:InsuranceTypes:0"] = "MOTOR ALL RISK";
        settings["Broker:InsuranceTypes:1"] = "MOTOR TOTAL LOSS";
        settings["Broker:InsuranceTypes:2"] = "PLACEHOLDER-TYPE-3";
        settings["Broker:EmailRouting:MOTOR ALL RISK"] = "PLACEHOLDER-recipient-1@example.invalid";
        settings["Broker:EmailRouting:MOTOR TOTAL LOSS"] = "PLACEHOLDER-recipient-2@example.invalid";
        settings["Broker:EmailRouting:PLACEHOLDER-TYPE-3"] = "PLACEHOLDER-recipient-3@example.invalid";
        return settings;
    }
}
