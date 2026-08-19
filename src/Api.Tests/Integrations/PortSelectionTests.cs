using Api.Composition;
using Api.Integrations.Next3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Tests.Integrations;

/// <summary>
/// design.md §6.2's config selection: which implementation is live is a config value, and nothing
/// outside the composition root knows which one it got.
/// </summary>
public sealed class PortSelectionTests
{
    [Fact]
    public void Next3Mode_Fake_ResolvesFakeClient()
    {
        var client = Resolve<INext3Client>(("Next3:Mode", "fake"));

        Assert.IsType<FakeNext3Client>(client);
    }

    [Fact]
    public void Next3Mode_Real_ResolvesRealClient()
    {
        // Resolvable but not usable: every method throws until slice 3.3 (#1 sandbox).
        var client = Resolve<INext3Client>(("Next3:Mode", "real"));

        Assert.IsType<RealNext3Client>(client);
    }

    [Fact]
    public void Next3Mode_Unknown_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Resolve<INext3Client>(("Next3:Mode", "sandbox")));

        Assert.Contains("Next3:Mode", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AssignmentSource_Fake_ResolvesFakeSource()
    {
        var source = Resolve<IAssignmentSource>(("Next3:AssignmentSource", "fake"));

        Assert.IsType<FakeAssignmentSource>(source);
    }

    [Theory]
    [InlineData("webhook")]
    [InlineData("poll")]
    public void AssignmentSource_DesignedButUnbuilt_ThrowsNamingTheOpenQuestion(string mode)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Resolve<IAssignmentSource>(("Next3:AssignmentSource", mode)));

        Assert.Contains("#34", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AssignmentSource_Unknown_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Resolve<IAssignmentSource>(("Next3:AssignmentSource", "carrier-pigeon")));

        Assert.Contains("Next3:AssignmentSource", ex.Message, StringComparison.Ordinal);
    }

    private static T Resolve<T>(params (string Key, string Value)[] overrides)
        where T : notnull
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "Server=(localdb)\\MSSQLLocalDB;Database=Unused;Integrated Security=true",
            ["Auth:Jwt:Issuer"] = "test",
            ["Auth:Jwt:Audience"] = "test",
            ["Auth:Jwt:SigningKey"] = "test-signing-key-at-least-32-bytes-long",
            ["Auth:Jwt:AccessTokenMinutes"] = "15",
            ["Auth:Jwt:RefreshTokenDays"] = "14",
        };

        foreach (var (key, value) in overrides)
        {
            settings[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAxaMotorClaims(configuration);

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<T>();
    }
}
