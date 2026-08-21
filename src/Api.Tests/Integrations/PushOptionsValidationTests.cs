using Api.Composition;
using Api.Integrations.Push;
using Api.Tests.Integration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Api.Tests.Integrations;

/// <summary>
/// The fail-fast for `Push:Mode = webpush` (design.md §8, slice 3.4) — the `Next3OptionsValidator`
/// shape, second time.
///
/// **Why it must be `ValidateOnStart` rather than a check inside the sender**: the sender runs inside
/// `AssignmentHandler`, which catches everything and turns it into "notified_at stays null". A
/// misconfigured key would therefore surface as experts quietly not being told about claims — the
/// failure this application exists to prevent — instead of as a deployment that refused to start.
/// </summary>
public sealed class PushOptionsValidationTests
{
    [Fact]
    public async Task WebPushMode_WithPlaceholders_FailsAtHostStartup()
    {
        using var host = BuildHost(Placeholders(PushModes.WebPush));

        var ex = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains("Push:Vapid:Subject", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Push:Vapid:PublicKey", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Push:Vapid:PrivateKey", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FakeMode_StartsOnTheSamePlaceholders()
    {
        // The regression that matters most here. Every environment runs `fake` on the placeholder
        // VAPID values, so a validator that ran unconditionally would stop the whole application
        // booting — every integration test, the week-4 demo, a fresh clone.
        using var host = BuildHost(Placeholders(PushModes.Fake));

        await host.StartAsync();
        await host.StopAsync();
    }

    [Fact]
    public async Task WebPushMode_WithUsableKeys_Starts()
    {
        using var host = BuildHost(Usable());

        await host.StartAsync();
        await host.StopAsync();
    }

    [Fact]
    public void APrivateKeyIsNeverEchoedBackInAFailure()
    {
        var options = new PushOptions { Mode = PushModes.WebPush };
        options.Vapid.Subject = "mailto:ops@example.invalid";
        options.Vapid.PublicKey = VapidTestKeys.PublicKey;
        options.Vapid.PrivateKey = string.Empty;

        var result = new PushOptionsValidator().Validate(null, options);

        // Validation failures land in deployment logs. Every other message in that validator quotes
        // the offending value to make the problem obvious; this one deliberately does not, because
        // the value is a credential that lets anyone send notifications browsers accept as AXA's.
        Assert.True(result.Failed);
        Assert.Contains("Push:Vapid:PrivateKey", result.FailureMessage!, StringComparison.Ordinal);
        Assert.Contains("user-secrets", result.FailureMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public void AShortPublicKeyIsRejectedWithoutQuotingIt()
    {
        var options = new PushOptions { Mode = PushModes.WebPush };
        options.Vapid.Subject = "mailto:ops@example.invalid";
        options.Vapid.PublicKey = VapidTestKeys.PublicKey[..80];
        options.Vapid.PrivateKey = VapidTestKeys.PrivateKey;

        var result = new PushOptionsValidator().Validate(null, options);

        // A truncated key is accepted by the browser's subscribe call and then fails to decrypt on
        // the device, with nothing server-side to see — the same class of silent failure that made
        // `Clarity.BlurAnalysisMaxEdge` a placeholder key in slice 2.5.
        Assert.True(result.Failed);
        Assert.Contains("Push:Vapid:PublicKey", result.FailureMessage!, StringComparison.Ordinal);
        Assert.Contains("80 characters", result.FailureMessage!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ops@example.invalid")]
    [InlineData("PLACEHOLDER")]
    [InlineData("")]
    public void ASubjectThatIsNotAContactIsRejected(string subject)
    {
        var options = new PushOptions { Mode = PushModes.WebPush };
        options.Vapid.Subject = subject;
        options.Vapid.PublicKey = VapidTestKeys.PublicKey;
        options.Vapid.PrivateKey = VapidTestKeys.PrivateKey;

        var result = new PushOptionsValidator().Validate(null, options);

        // RFC 8292 wants a mailto: or https: contact, and some push services reject a request
        // without one — which would look like "notifications stopped working" rather than like a
        // configuration error.
        Assert.True(result.Failed);
        Assert.Contains("Push:Vapid:Subject", result.FailureMessage!, StringComparison.Ordinal);
    }

    private static Dictionary<string, string?> Placeholders(string mode) => new(StringComparer.Ordinal)
    {
        // The `Push` section of appsettings.Placeholders.json, as checked in.
        ["Push:Mode"] = mode,
        ["Push:Vapid:Subject"] = "mailto:PLACEHOLDER-push-contact@example.invalid",
        ["Push:Vapid:PublicKey"] = "PLACEHOLDER-vapid-public-key",
        ["Push:Vapid:PrivateKey"] = "PLACEHOLDER-vapid-private-key",
        ["Push:TimeoutSeconds"] = "15",
    };

    private static Dictionary<string, string?> Usable()
    {
        var settings = Placeholders(PushModes.WebPush);
        settings["Push:Vapid:Subject"] = "mailto:ops@example.invalid";
        settings["Push:Vapid:PublicKey"] = VapidTestKeys.PublicKey;
        settings["Push:Vapid:PrivateKey"] = VapidTestKeys.PrivateKey;
        return settings;
    }

    private static IHost BuildHost(Dictionary<string, string?> settings)
    {
        settings["ConnectionStrings:Default"] =
            "Server=(localdb)\\MSSQLLocalDB;Database=Unused;Integrated Security=true";
        settings["Auth:Jwt:Issuer"] = "test";
        settings["Auth:Jwt:Audience"] = "test";
        settings["Auth:Jwt:SigningKey"] = "test-signing-key-at-least-32-bytes-long";
        settings["Auth:Jwt:AccessTokenMinutes"] = "15";
        settings["Auth:Jwt:RefreshTokenDays"] = "14";
        settings["Outbox:WorkerEnabled"] = "false";
        settings["Retention:CleanupEnabled"] = "false";

        return new HostBuilder()
            .ConfigureAppConfiguration(b => b.AddInMemoryCollection(settings))
            .ConfigureServices((context, services) =>
            {
                services.AddLogging();
                services.AddAxaMotorClaims(context.Configuration);
            })
            .Build();
    }
}
