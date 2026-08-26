using Api.Composition;
using Api.Integrations.Next3;
using Api.Integrations.Push;
using Api.Modules.PublicSurface;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
    public void PushMode_Fake_ResolvesFakeSender()
    {
        var sender = Resolve<IPushSender>(("Push:Mode", "fake"));

        Assert.IsType<FakePushSender>(sender);
    }

    [Fact]
    public void PushMode_WebPush_ResolvesTheCompositeOverWebPushAlone()
    {
        // Resolvable without any Push:Vapid:* settings, exactly as Next3Mode_Real_ResolvesRealClient
        // is: validation lives in ValidateOnStart rather than in a constructor, so this test proves
        // the DI switch and nothing else (slice 3.4).
        //
        // **Moved deliberately in slice 6.3**: it asserted `IsType<WebPushSender>`, and the live mode
        // now composes every channel the deployment has. The assertion is not relaxed — it is
        // sharpened, because "the real sender" was never the interesting property. What matters is
        // that with FCM off the composite carries web push *and nothing else*, so this slice is a
        // no-op for every environment until somebody turns FCM on.
        var sender = Resolve<IPushSender>(("Push:Mode", "webpush"));

        var composite = Assert.IsType<CompositePushSender>(sender);
        Assert.IsType<WebPushSender>(Assert.Single(composite.Channels));
    }

    [Fact]
    public void PushMode_WebPushWithFcm_AddsTheNativeChannelBesideWebPush()
    {
        // The Android half of §8, and the only place `Push:Fcm:Enabled` is read. Both shapes resolve
        // an IPushSender and both behave identically until a handset registers, so a typo in that
        // key would otherwise ship a deployment whose Android push silently does not exist — the
        // exact class of single-platform silent outage slice 6.3a found on iOS.
        //
        // Order matters and is asserted: web push first, FCM second. Both are always attempted, but
        // a browser is the cheaper call and the one every role has.
        var sender = Resolve<IPushSender>(("Push:Mode", "webpush"), ("Push:Fcm:Enabled", "true"));

        var composite = Assert.IsType<CompositePushSender>(sender);
        Assert.Collection(
            composite.Channels,
            channel => Assert.IsType<WebPushSender>(channel),
            channel => Assert.IsType<FcmPushSender>(channel));
    }

    [Fact]
    public void PushMode_Defaults_ToTheFake()
    {
        // No Push section at all — the state a fresh clone and every test run is in. Web push needs a
        // VAPID key pair, and nothing should require one just to boot.
        Assert.IsType<FakePushSender>(Resolve<IPushSender>());
    }

    [Fact]
    public void PushMode_Unknown_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Resolve<IPushSender>(("Push:Mode", "apns")));

        Assert.Contains("Push:Mode", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PublicLinkOptions_BindFromTheirSection()
    {
        // Catches a typo'd SectionName without booting a host: silently-zero limits would mean an
        // unlimited public surface (§9.1) and a MaxFiles of 0 rejecting every submission.
        var options = Resolve<IOptions<PublicLinkOptions>>(
            ("PublicLink:ValidityDays", "7"),
            ("PublicLink:MaxFiles", "15"),
            ("PublicLink:MaxFileMb", "10"),
            ("PublicLink:RateLimit:PerIpPermitsPerMinute", "60"),
            ("PublicLink:RateLimit:PerTokenPermitsPerMinute", "20")).Value;

        Assert.Equal(7, options.ValidityDays);
        Assert.Equal(15, options.MaxFiles);
        Assert.Equal(10, options.MaxFileMb);
        Assert.Equal(60, options.RateLimit.PerIpPermitsPerMinute);
        Assert.Equal(20, options.RateLimit.PerTokenPermitsPerMinute);
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
