using Api.Composition;
using Api.Integrations.Next3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Api.Tests.Integrations;

/// <summary>
/// The fail-fast for `Next3:Mode = real` (design.md §6.2, #1).
///
/// **The regression this file mostly exists to prevent is in the other direction.** Every environment
/// runs `Next3:Mode = fake` on a placeholder file where `BaseUrl` is `https://PLACEHOLDER-next3.example`
/// and `AuthMode` is the literal string `PLACEHOLDER`. A validator that ran unconditionally would stop
/// the application booting at all — every integration test, the week-4 demo, the developer's machine.
/// So the last test here matters as much as the first.
/// </summary>
public sealed class Next3OptionsValidationTests
{
    [Fact]
    public void RealMode_WithPlaceholders_FailsFastAndNamesTheKey()
    {
        var ex = Assert.Throws<OptionsValidationException>(() => ResolveOptions(Placeholders("real")));

        // Named keys, not "configuration is invalid". Whoever sees this is switching an environment to
        // a live NEXT3 for the first time, probably at a distance from the person who wrote the config.
        Assert.Contains("Next3:BaseUrl", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Next3:AuthMode", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Next3:ArrivalTimeZone", ex.Message, StringComparison.Ordinal);

        // And the open question, so the reader knows this is waiting on AXA rather than broken.
        Assert.Contains("#1", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FakeMode_WithTheSamePlaceholders_BootsUnchanged()
    {
        // The whole placeholder file, in the mode every environment actually runs. If this ever goes
        // red, nothing in the application starts.
        var options = ResolveOptions(Placeholders("fake"));

        Assert.Equal("https://PLACEHOLDER-next3.example", options.BaseUrl);
    }

    [Fact]
    public void RealMode_WithUsableSettings_Resolves()
    {
        var settings = Placeholders("real");
        settings["Next3:BaseUrl"] = "https://next3.test.invalid/api";
        settings["Next3:AuthMode"] = Next3AuthModes.ApiKey;
        settings["Next3:ApiKey"] = "a-key";
        settings["Next3:ArrivalTimeZone"] = "Asia/Dubai";

        var options = ResolveOptions(settings);

        Assert.Equal("Asia/Dubai", options.ArrivalTimeZone);
    }

    [Fact]
    public void OAuthMode_RequiresItsCredentials()
    {
        var settings = Usable();
        settings["Next3:AuthMode"] = Next3AuthModes.OAuth;

        var ex = Assert.Throws<OptionsValidationException>(() => ResolveOptions(settings));

        Assert.Contains("Next3:OAuth:TokenUrl", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Next3:OAuth:ClientId", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OAuthMode_WithItsCredentials_Resolves()
    {
        var settings = Usable();
        settings["Next3:AuthMode"] = Next3AuthModes.OAuth;
        settings["Next3:OAuth:TokenUrl"] = "https://next3.test.invalid/api/auth/token";
        settings["Next3:OAuth:ClientId"] = "an-id";
        settings["Next3:OAuth:ClientSecret"] = "a-secret";

        Assert.Equal(Next3AuthModes.OAuth, ResolveOptions(settings).AuthMode);
    }

    [Fact]
    public void AnUnresolvableTimeZone_IsRefusedRatherThanDefaultedToUtc()
    {
        var settings = Usable();
        settings["Next3:ArrivalTimeZone"] = "Not/AZone";

        var ex = Assert.Throws<OptionsValidationException>(() => ResolveOptions(settings));

        // The alternative to failing here is falling back to UTC, and that is the precise defect slice
        // 2.4 removed: an expert arriving at 01:30 GST reported to NEXT3 as arriving the previous
        // calendar day, on the field a claims dispute turns on, in a row nobody can repair afterwards.
        Assert.Contains("Next3:ArrivalTimeZone", ex.Message, StringComparison.Ordinal);
        Assert.Contains("#6", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AWellFormedPlaceholderUrl_IsStillTreatedAsUnset()
    {
        var settings = Usable();
        settings["Next3:BaseUrl"] = "https://PLACEHOLDER-next3.example";

        // It parses, it is absolute, it is https — and it does not exist. Shape checks alone would
        // wave this through and the failure would surface hours later as a queue of `failed` rows.
        var ex = Assert.Throws<OptionsValidationException>(() => ResolveOptions(settings));

        Assert.Contains("Next3:BaseUrl", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ANonPositiveTimeout_IsRefused()
    {
        var settings = Usable();
        settings["Next3:TimeoutSeconds"] = "0";

        var ex = Assert.Throws<OptionsValidationException>(() => ResolveOptions(settings));

        Assert.Contains("Next3:TimeoutSeconds", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RealMode_FailsAtHostStartup_NotAtFirstUse()
    {
        // ValidateOnStart is the difference between "the operator sees this in the deployment log" and
        // "the first expert to press Arrived finds out, hours later, as a `failed` outbox row". Every
        // test above resolves the options directly and would still pass if ValidateOnStart were
        // dropped — so this one starts a real host and asserts the failure happens *there*.
        using var host = BuildHost(Placeholders("real"));

        var ex = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains("Next3:BaseUrl", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FakeMode_StartsTheHostOnPlaceholders()
    {
        // The other direction, and the one that would take the whole application down with it.
        using var host = BuildHost(Placeholders("fake"));

        await host.StartAsync();
        await host.StopAsync();
    }

    /// <summary>The `Next3` section of `appsettings.Placeholders.json`, as checked in.</summary>
    private static Dictionary<string, string?> Placeholders(string mode) => new(StringComparer.Ordinal)
    {
        ["Next3:Mode"] = mode,
        ["Next3:BaseUrl"] = "https://PLACEHOLDER-next3.example",
        ["Next3:AuthMode"] = "PLACEHOLDER",
        ["Next3:ApiKey"] = "PLACEHOLDER-next3-api-key",
        ["Next3:OAuth:TokenUrl"] = "https://PLACEHOLDER-next3.example/auth/token",
        ["Next3:OAuth:ClientId"] = "PLACEHOLDER-client-id",
        ["Next3:OAuth:ClientSecret"] = "PLACEHOLDER-client-secret",
        ["Next3:ArrivalTimeZone"] = "PLACEHOLDER-IANA-ZONE",
        ["Next3:TimeoutSeconds"] = "30",
    };

    private static Dictionary<string, string?> Usable()
    {
        var settings = Placeholders("real");
        settings["Next3:BaseUrl"] = "https://next3.test.invalid/api";
        settings["Next3:AuthMode"] = Next3AuthModes.ApiKey;
        settings["Next3:ApiKey"] = "a-key";
        settings["Next3:ArrivalTimeZone"] = "Asia/Dubai";
        return settings;
    }

    /// <summary>
    /// Builds the real container and resolves the options, which is what runs the validator.
    /// <c>ValidateOnStart</c> only moves that moment to host start-up; the check itself is the same
    /// one, and this needs no database.
    /// </summary>
    private static void Fill(Dictionary<string, string?> settings)
    {
        settings["ConnectionStrings:Default"] =
            "Server=(localdb)\\MSSQLLocalDB;Database=Unused;Integrated Security=true";
        settings["Auth:Jwt:Issuer"] = "test";
        settings["Auth:Jwt:Audience"] = "test";
        settings["Auth:Jwt:SigningKey"] = "test-signing-key-at-least-32-bytes-long";
        settings["Auth:Jwt:AccessTokenMinutes"] = "15";
        settings["Auth:Jwt:RefreshTokenDays"] = "14";

        // Slice 5.2: BrokerOptionsValidator runs in every mode, so a host with no `Broker`
        // section no longer starts. Supplied here exactly as the Jwt block above is.
        settings.WithBroker();
    }

    /// <summary>
    /// Starts a real host, which is what makes <c>ValidateOnStart</c> observable — see the two tests
    /// that use it. The workers are switched off for the same reason <c>ApiFixture</c> switches them
    /// off: a live loop would tick against a database this test has no interest in.
    /// </summary>
    private static IHost BuildHost(Dictionary<string, string?> settings)
    {
        Fill(settings);
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

    private static Next3Options ResolveOptions(Dictionary<string, string?> settings)
    {
        Fill(settings);

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAxaMotorClaims(configuration);

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<Next3Options>>().Value;
    }
}
