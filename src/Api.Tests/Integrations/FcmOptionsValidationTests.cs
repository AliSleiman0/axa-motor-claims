using Api.Composition;
using Api.Integrations.Push;
using Api.Tests.Integration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Api.Tests.Integrations;

/// <summary>
/// The fail-fast for `Push:Fcm` (design.md §8, slice 6.3) — the <c>PushOptionsValidator</c> shape,
/// third time, and the first one gated **twice**: live mode *and* the feature switched on.
///
/// **The second gate is what keeps Appendix A honest.** `Push:Fcm:Enabled` is false everywhere and
/// the shipped project id and key path are placeholders, so a validator that fired on mode alone
/// would have stopped every `webpush` deployment booting the moment this slice landed —
/// <see cref="WebPushMode_WithFcmDisabled_StartsOnThePlaceholders"/> is the regression that catches
/// it, and it is the same test <c>FakeMode_StartsOnTheSamePlaceholders</c> is one level up.
///
/// Why `ValidateOnStart` rather than a check inside the sender applies here with *more* force than
/// it did for web push: the FCM sender runs inside <c>CompositePushSender</c>, which is designed to
/// swallow one channel's failure so the other still delivers. A misconfiguration would therefore
/// surface as Android handsets quietly receiving nothing while desktop browsers kept working — the
/// single-platform silent outage slice 6.3a found on iOS and this slice exists to make impossible.
/// </summary>
public sealed class FcmOptionsValidationTests
{
    [Fact]
    public async Task WebPushMode_WithFcmEnabledOnPlaceholders_FailsAtHostStartup()
    {
        using var host = BuildHost(Enabled(placeholders: true));

        var ex = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains("Push:Fcm:ProjectId", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Push:Fcm:ServiceAccountJsonPath", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WebPushMode_WithFcmDisabled_StartsOnThePlaceholders()
    {
        // The regression that matters most. Every environment ships `Enabled: false` over placeholder
        // FCM values, so a validator that ran on mode alone would stop the application booting
        // wherever web push is live — which is the whole test environment.
        using var host = BuildHost(Enabled(placeholders: true, enabled: false));

        await host.StartAsync();
        await host.StopAsync();
    }

    [Fact]
    public async Task FakeMode_IgnoresFcmEntirelyEvenWhenItIsSwitchedOn()
    {
        // Two gates, not one: `fake` never builds a composite at all, so an FCM block left switched
        // on in a fake environment is inert rather than fatal. Pins that the outer gate is the mode.
        using var host = BuildHost(Enabled(placeholders: true), mode: PushModes.Fake);

        await host.StartAsync();
        await host.StopAsync();
    }

    [Fact]
    public async Task WebPushMode_WithUsableFcmSettings_Starts()
    {
        using var host = BuildHost(Enabled(placeholders: false));

        await host.StartAsync();
        await host.StopAsync();
    }

    [Fact]
    public void AServiceAccountPathPointingAtNothingIsRejected()
    {
        var options = Options(enabled: true);
        options.Fcm.ProjectId = "axa-motor-claims-fcm";
        options.Fcm.ServiceAccountJsonPath =
            Path.Combine(AppContext.BaseDirectory, "no-such-service-account.json");

        var result = new FcmOptionsValidator().Validate(null, options);

        // Checked at startup rather than at the first send, because at send time a missing file is
        // one caught exception inside a composite built to keep going — it would read as "Android is
        // quiet today". In a container the likeliest cause is a secret declared and never mounted,
        // so the message says so.
        Assert.True(result.Failed);
        Assert.Contains("points at no file", result.FailureMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public void AZeroTimeoutIsRejected()
    {
        var options = Options(enabled: true);
        options.Fcm.ProjectId = "axa-motor-claims-fcm";
        options.Fcm.ServiceAccountJsonPath = FcmTestServiceAccount.Path;
        options.Fcm.TimeoutSeconds = 0;

        var result = new FcmOptionsValidator().Validate(null, options);

        Assert.True(result.Failed);
        Assert.Contains("Push:Fcm:TimeoutSeconds", result.FailureMessage!, StringComparison.Ordinal);
    }

    [Fact]
    public void TheValidatorItselfOnlyEverInspectsTheFcmBlock()
    {
        // Deliberately paired with the registration gate rather than trusted alongside it. This
        // validator is registered *in addition to* PushOptionsValidator on the same options type, so
        // if it also judged the VAPID values it would double-report them — and, worse, would make
        // `Push:Fcm:Enabled` able to fail a deployment over settings that have nothing to do with it.
        var options = Options(enabled: true);
        options.Fcm.ProjectId = "axa-motor-claims-fcm";
        options.Fcm.ServiceAccountJsonPath = FcmTestServiceAccount.Path;
        options.Vapid.Subject = "not-a-contact";
        options.Vapid.PublicKey = string.Empty;
        options.Vapid.PrivateKey = string.Empty;

        Assert.False(new FcmOptionsValidator().Validate(null, options).Failed);
    }

    private static PushOptions Options(bool enabled)
    {
        var options = new PushOptions { Mode = PushModes.WebPush };
        options.Fcm.Enabled = enabled;
        return options;
    }

    private static Dictionary<string, string?> Enabled(bool placeholders, bool enabled = true) =>
        new(StringComparer.Ordinal)
        {
            ["Push:Fcm:Enabled"] = enabled ? "true" : "false",
            ["Push:Fcm:ProjectId"] = placeholders
                ? "PLACEHOLDER-firebase-project-id"
                : "axa-motor-claims-fcm",
            ["Push:Fcm:ServiceAccountJsonPath"] = placeholders
                ? "PLACEHOLDER-fcm-service-account-json-path"
                : FcmTestServiceAccount.Path,
            ["Push:Fcm:TimeoutSeconds"] = "15",
        };

    private static IHost BuildHost(Dictionary<string, string?> fcm, string mode = PushModes.WebPush)
    {
        var settings = new Dictionary<string, string?>(fcm, StringComparer.Ordinal)
        {
            // Usable VAPID values throughout, so any failure reported here is unambiguously the FCM
            // validator's rather than PushOptionsValidator's — they run on the same options type.
            ["Push:Mode"] = mode,
            ["Push:Vapid:Subject"] = "mailto:ops@motor-claims.dev",
            ["Push:Vapid:PublicKey"] = VapidTestKeys.PublicKey,
            ["Push:Vapid:PrivateKey"] = VapidTestKeys.PrivateKey,
            ["Push:TimeoutSeconds"] = "15",
            ["ConnectionStrings:Default"] =
                "Server=(localdb)\\MSSQLLocalDB;Database=Unused;Integrated Security=true",
            ["Auth:Jwt:Issuer"] = "test",
            ["Auth:Jwt:Audience"] = "test",
            ["Auth:Jwt:SigningKey"] = "test-signing-key-at-least-32-bytes-long",
            ["Auth:Jwt:AccessTokenMinutes"] = "15",
            ["Auth:Jwt:RefreshTokenDays"] = "14",
            ["Outbox:WorkerEnabled"] = "false",
            ["Retention:CleanupEnabled"] = "false",
        };

        // BrokerOptionsValidator runs in every mode, so a host with no `Broker` section cannot start.
        settings.WithBroker();

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
