using Api.Modules.Broker;
using Microsoft.Extensions.Options;

namespace Api.Tests.Unit;

/// <summary>
/// design.md Appendix A's `Broker` section (#13, #14), and the validator that runs over it in **every**
/// environment.
///
/// The first test is the one that matters most, exactly as it is in `Next3OptionsValidationTests`: the
/// placeholders every environment runs on today have to pass, or nothing starts anywhere. The rest are
/// the misconfigurations that would otherwise surface as a 500 for one insurance type only, seen by a
/// broker rather than by whoever wrote the config.
/// </summary>
public class BrokerOptionsTests
{
    [Fact]
    public void ThePlaceholderFileValidates()
    {
        Assert.True(Validate(Placeholders()).Succeeded);
    }

    [Fact]
    public void AnEmptyTypeList_IsRefused()
    {
        var options = Placeholders();
        options.InsuranceTypes.Clear();
        options.EmailRouting.Clear();

        Assert.Contains("Broker:InsuranceTypes", Failure(options), StringComparison.Ordinal);
    }

    [Fact]
    public void ATypeWithNoRoute_IsRefusedAndNamed()
    {
        var options = Placeholders();
        options.InsuranceTypes.Add("PLACEHOLDER-TYPE-4");

        var failure = Failure(options);

        // Named, not "configuration is invalid": whoever reads this is adding a product to the list
        // and has forgotten the desk it goes to.
        Assert.Contains("Broker:EmailRouting", failure, StringComparison.Ordinal);
        Assert.Contains("PLACEHOLDER-TYPE-4", failure, StringComparison.Ordinal);
        Assert.Contains("#13", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void ARouteWithNoType_IsRefused()
    {
        // The same mistake seen from the other end, and the reason both halves are checked: a routing
        // key nothing offers is dead config, and the likeliest cause is that the type beside it is
        // misspelt — in which case the *type* has no route and a broker choosing it gets nowhere.
        var options = Placeholders();
        options.EmailRouting["MOTOR ALL RSIK"] = "PLACEHOLDER-recipient-9@example.invalid";

        Assert.Contains("MOTOR ALL RSIK", Failure(options), StringComparison.Ordinal);
    }

    [Fact]
    public void ADuplicateType_IsRefused()
    {
        var options = Placeholders();
        options.InsuranceTypes.Add(BrokerFlowsTypes.AllRisk);

        Assert.Contains("more than once", Failure(options), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-address")]
    [InlineData("missing@host")]
    public void AMalformedRecipient_IsRefused(string recipient)
    {
        var options = Placeholders();
        options.EmailRouting[BrokerFlowsTypes.AllRisk] = recipient;

        Assert.Contains("Broker:EmailRouting", Failure(options), StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnroutableButWellFormedRecipient_Passes()
    {
        // `.invalid` is reserved precisely so it can never resolve, and every placeholder recipient
        // uses it. A validator that reached for deliverability would refuse to boot on the file every
        // environment runs — the mistake `Next3OptionsValidator` documents from the other direction.
        var options = Placeholders();

        Assert.True(Validate(options).Succeeded);
        Assert.EndsWith(
            "@example.invalid", options.EmailRouting[BrokerFlowsTypes.AllRisk], StringComparison.Ordinal);
    }

    private static string Failure(BrokerOptions options)
    {
        var result = Validate(options);
        Assert.True(result.Failed);
        return result.FailureMessage!;
    }

    private static ValidateOptionsResult Validate(BrokerOptions options) =>
        new BrokerOptionsValidator().Validate(null, options);

    /// <summary>The `Broker` section of `appsettings.Placeholders.json`, as checked in.</summary>
    private static BrokerOptions Placeholders()
    {
        var options = new BrokerOptions { AllowUpload = true };
        options.InsuranceTypes.Add(BrokerFlowsTypes.AllRisk);
        options.InsuranceTypes.Add("MOTOR TOTAL LOSS");
        options.InsuranceTypes.Add("PLACEHOLDER-TYPE-3");
        options.EmailRouting[BrokerFlowsTypes.AllRisk] = "PLACEHOLDER-recipient-1@example.invalid";
        options.EmailRouting["MOTOR TOTAL LOSS"] = "PLACEHOLDER-recipient-2@example.invalid";
        options.EmailRouting["PLACEHOLDER-TYPE-3"] = "PLACEHOLDER-recipient-3@example.invalid";
        return options;
    }
}

/// <summary>The two BRD examples, named once so the unit tests and the integration flows agree.</summary>
internal static class BrokerFlowsTypes
{
    public const string AllRisk = "MOTOR ALL RISK";
}
