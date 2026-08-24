using Api.Composition;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Api.Tests.Integrations;

/// <summary>
/// That `Broker`'s validation is wired to <c>ValidateOnStart</c>, not merely written.
///
/// <c>BrokerOptionsTests</c> exercises the rules directly and would still pass if the registration
/// were dropped — which is exactly the gap slice 3.3 opened this lane for. These two tests start a
/// real host: one asserts a misconfiguration stops it, and the other asserts the checked-in
/// placeholders do not. **The second is the one that would take the whole application down**, and it
/// is why an always-on validator was safe to register at all.
/// </summary>
public sealed class BrokerOptionsValidationTests
{
    [Fact]
    public async Task ATypeWithNoRoute_FailsAtHostStartup()
    {
        var settings = Settings();
        settings["Broker:InsuranceTypes:3"] = "PLACEHOLDER-TYPE-4";

        using var host = BuildHost(settings);

        var ex = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains("Broker:EmailRouting", ex.Message, StringComparison.Ordinal);
        Assert.Contains("PLACEHOLDER-TYPE-4", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThePlaceholders_StartTheHost()
    {
        using var host = BuildHost(Settings());

        await host.StartAsync();
        await host.StopAsync();
    }

    private static Dictionary<string, string?> Settings() =>
        new Dictionary<string, string?>(StringComparer.Ordinal).WithBroker();

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
