using Api.Infrastructure;
using Api.Infrastructure.Cleanup;
using Api.Integrations;
using Api.Integrations.Blob;
using Api.Integrations.Email;
using Api.Integrations.Push;
using Api.Integrations.Sms;
using Api.Modules.Broker;
using Api.Modules.Media;
using Api.Modules.PublicSurface;
using Api.Outbox;
using Api.Tests.Integrations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Api.Tests.Integration;

/// <summary>
/// One LocalDB database + one booted app per test run. The connection string is injected via the
/// env-var lane (Program.cs re-adds env vars last, so it deterministically beats appsettings and
/// placeholders). Env vars are process-global, so all integration classes share one serialized
/// xUnit collection; data isolation is by unique phone number per test.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    private WebApplicationFactory<Program>? _factory;
    private string _connectionString = string.Empty;

    public CapturingSmsSender Sms { get; } = new();

    /// <summary>
    /// The booted app's <see cref="IEmailSender"/> (slice 5.2). It wraps the real fake rather than
    /// replacing it, so every `notification` assertion is untouched — what it adds is the attachment
    /// **bytes**, which §4 deliberately keeps out of the notification payload.
    /// </summary>
    public CapturingEmailSender Email => (CapturingEmailSender)Services.GetRequiredService<IEmailSender>();

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero));

    public IServiceProvider Services => Factory.Services;

    /// <summary>
    /// For the few tests that need raw ADO rather than EF — the READPAST dequeue has to be driven
    /// from two real connections with an open transaction on one of them.
    /// </summary>
    public string ConnectionString => _connectionString;

    /// <summary>
    /// The live <see cref="PublicLinkOptions"/>, mutable mid-test. Changing a limit only affects
    /// rate-limit partitions created afterwards, so a test that lowers one must also use a fresh
    /// token and a fresh <c>X-Test-Ip</c>.
    /// </summary>
    public MutableOptionsMonitor<PublicLinkOptions> PublicLink =>
        (MutableOptionsMonitor<PublicLinkOptions>)Services
            .GetRequiredService<IOptionsMonitor<PublicLinkOptions>>();

    /// <summary>
    /// The live <see cref="FakeOptions"/>, mutable mid-test — the only way to make the booted app's
    /// NEXT3 fail. One caution: <c>FakeBehavior</c> is a single instance shared by NEXT3 <em>and</em>
    /// all three senders, and the integration classes run as one serialized collection, so a test
    /// that raises the failure rate must restore it or it will poison every test that follows.
    /// </summary>
    public MutableOptionsMonitor<FakeOptions> Fake =>
        (MutableOptionsMonitor<FakeOptions>)Services.GetRequiredService<IOptionsMonitor<FakeOptions>>();

    /// <summary>
    /// The live <see cref="OutboxOptions"/>, mutable mid-test — so a retry test can shorten
    /// <c>MaxAttempts</c> or a concurrency test can widen <c>BatchSize</c> without a second host.
    /// Restore it in a <c>finally</c> for the same reason <see cref="Fake"/> must be restored.
    /// </summary>
    public MutableOptionsMonitor<OutboxOptions> Outbox =>
        (MutableOptionsMonitor<OutboxOptions>)Services.GetRequiredService<IOptionsMonitor<OutboxOptions>>();

    /// <summary>
    /// The live <see cref="MediaOptions"/> and <see cref="RetentionOptions"/>, mutable mid-test — so a
    /// cap or a retention window can be moved without a second host. Restore in a <c>finally</c>, as
    /// with <see cref="Fake"/>; <c>MediaFlows.WithMediaOptions</c> does it for you.
    /// </summary>
    public MutableOptionsMonitor<MediaOptions> Media =>
        (MutableOptionsMonitor<MediaOptions>)Services.GetRequiredService<IOptionsMonitor<MediaOptions>>();

    public MutableOptionsMonitor<RetentionOptions> Retention =>
        (MutableOptionsMonitor<RetentionOptions>)Services
            .GetRequiredService<IOptionsMonitor<RetentionOptions>>();

    /// <summary>
    /// The live <see cref="ClarityOptions"/>, mutable mid-test — so the config endpoint can be proved
    /// to project configuration rather than a constant baked in at build time (slice 2.5).
    /// </summary>
    public MutableOptionsMonitor<ClarityOptions> Clarity =>
        (MutableOptionsMonitor<ClarityOptions>)Services
            .GetRequiredService<IOptionsMonitor<ClarityOptions>>();

    /// <summary>
    /// §5.3's broker settings (slice 5.2). Mutable above all for the BRD's upload kill-switch: the
    /// whole point of `Broker:AllowUpload` is that it changes without a restart, and a test that could
    /// not flip it in place would be proving something else. Restore it in a <c>finally</c>, as with
    /// <see cref="Fake"/> — <c>BrokerFlows.WithUploadSwitch</c> does it for you.
    /// </summary>
    public MutableOptionsMonitor<BrokerOptions> Broker =>
        (MutableOptionsMonitor<BrokerOptions>)Services
            .GetRequiredService<IOptionsMonitor<BrokerOptions>>();

    /// <summary>
    /// §8's push settings (slice 3.4). Mutable so a test can read the subscription cap rather than
    /// hardcoding a number that would silently stop meaning the same thing if the config changed.
    /// </summary>
    public MutableOptionsMonitor<PushOptions> Push =>
        (MutableOptionsMonitor<PushOptions>)Services
            .GetRequiredService<IOptionsMonitor<PushOptions>>();

    /// <summary>
    /// The §6.3 worker loop body. Tests drive it a pass at a time rather than letting the background
    /// service tick — see the <c>Outbox__WorkerEnabled</c> note in <see cref="InitializeAsync"/>.
    /// </summary>
    public OutboxProcessor OutboxProcessor => Services.GetRequiredService<OutboxProcessor>();

    /// <summary>§7.3's sweeps, driven a pass at a time for exactly the same reason.</summary>
    public CleanupRunner Cleanup => Services.GetRequiredService<CleanupRunner>();

    /// <summary>
    /// The transit buffer the booted app actually writes to. <c>Blob:Mode</c> stays `fake`, so the
    /// whole suite runs without Azurite; <c>BlobStoreContractTests</c> is what exercises the real
    /// adapter, and only when the emulator is up.
    /// </summary>
    public InMemoryBlobStore Blobs => Services.GetRequiredService<InMemoryBlobStore>();

    /// <summary>
    /// The same store as <see cref="Blobs"/>, seen through the test-only decorator that can stall one
    /// <c>Put</c> (slice 7.1). Inert unless a test arms it, so nothing else in the suite is affected —
    /// <see cref="RemoteIpTestFilter"/>'s arrangement, second outing.
    /// </summary>
    internal GatingBlobStore BlobGate => (GatingBlobStore)Services.GetRequiredService<IBlobStore>();

    private WebApplicationFactory<Program> Factory =>
        _factory ?? throw new InvalidOperationException("Fixture not initialized.");

    public HttpClient CreateClient() => Factory.CreateClient();

    public AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(_connectionString).Options;
        return new AppDbContext(options);
    }

    public async Task InitializeAsync()
    {
        // CI has no LocalDB: point this env var at a SQL container there (design.md §10).
        var dbName = $"AxaMotorClaims_Test_{Guid.NewGuid():N}";
        _connectionString = $"Server=(localdb)\\MSSQLLocalDB;Database={dbName};Integrated Security=true";
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", _connectionString);

        // **Production, so the suite is hermetic against the developer's own machine.** Added slice
        // 3.4: `Program.cs` now re-adds user secrets after the placeholder file (it had to — the
        // placeholders were silently overriding them), and a test host running as Development would
        // therefore read whatever that developer happens to have in `dotnet user-secrets`. That is
        // not theoretical: it turned `TheVapidPublicKeyIsServedToAuthenticatedUsers` red on the
        // machine that had just set a real VAPID key for the browser pass, and would have passed
        // everywhere else. Nothing else in the application branches on the environment.
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");

        // The outbox worker is a registered hosted service, so without this it would tick every 30 s
        // against the database every test in this serialized collection shares — silently draining
        // rows the outbox tests are still asserting on. Tests call OutboxProcessor.RunOnce instead,
        // which is the same code the loop runs, just on the test's schedule.
        Environment.SetEnvironmentVariable("Outbox__WorkerEnabled", "false");

        // Same trap, same answer (slice 2.3): the cleanup worker is a registered hosted service, and
        // a live loop would sweep blobs and purge otp_challenge rows the suite is mid-assertion on.
        // Tests call CleanupRunner.RunOnce, which is the code the loop runs.
        Environment.SetEnvironmentVariable("Retention__CleanupEnabled", "false");

        await using (var db = CreateDbContext())
        {
            await db.Database.MigrateAsync();

            // Azure SQL Database has READ_COMMITTED_SNAPSHOT ON by default and LocalDB has it OFF,
            // so without this the concurrency tests — the outbox dequeue's READPAST claim, and 2.4's
            // "exactly one arrival" guard — prove their point under a locking protocol the
            // production database will not be running. Matching it here is the cheap half of that
            // gap; the expensive half (a real Azure SQL run) belongs to §10's test environment.
            // Suppressed rather than worked around: a database name cannot be a SQL parameter, so
            // EF1003 has no safe alternative to offer here. dbName is `AxaMotorClaims_Test_` plus a
            // Guid this method generated four lines above — it never leaves this process and holds
            // nothing but hex digits and underscores.
#pragma warning disable EF1003
            await db.Database.ExecuteSqlRawAsync(
                "ALTER DATABASE [" + dbName + "] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE");
#pragma warning restore EF1003
        }

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<ISmsSender>(Sms));
                services.Replace(ServiceDescriptor.Singleton<IEmailSender>(sp =>
                    new CapturingEmailSender(ActivatorUtilities.CreateInstance<FakeEmailSender>(sp))));
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(Time));

                // The rate limiter reads PublicLinkOptions per request, so swapping the monitor for
                // a mutable one lets a test lower §9.1's limits without booting a second host.
                // Seeded from the real bound values, so every other test sees production numbers.
                services.Replace(ServiceDescriptor.Singleton<IOptionsMonitor<PublicLinkOptions>>(sp =>
                    new MutableOptionsMonitor<PublicLinkOptions>(
                        sp.GetRequiredService<IOptions<PublicLinkOptions>>().Value)));
                // Same trick for failure injection: without it there is no way to make the booted
                // app's NEXT3 go down, and §4's staleness path could only be tested outside the host.
                services.Replace(ServiceDescriptor.Singleton<IOptionsMonitor<FakeOptions>>(sp =>
                    new MutableOptionsMonitor<FakeOptions>(
                        sp.GetRequiredService<IOptions<FakeOptions>>().Value)));
                // And again for the outbox, so a retry test can shorten MaxAttempts in place.
                services.Replace(ServiceDescriptor.Singleton<IOptionsMonitor<OutboxOptions>>(sp =>
                    new MutableOptionsMonitor<OutboxOptions>(
                        sp.GetRequiredService<IOptions<OutboxOptions>>().Value)));
                // And for §7's two: a size cap that a 15 MB upload would otherwise be needed to test,
                // and retention windows measured in days.
                services.Replace(ServiceDescriptor.Singleton<IOptionsMonitor<MediaOptions>>(sp =>
                    new MutableOptionsMonitor<MediaOptions>(
                        sp.GetRequiredService<IOptions<MediaOptions>>().Value)));
                services.Replace(ServiceDescriptor.Singleton<IOptionsMonitor<RetentionOptions>>(sp =>
                    new MutableOptionsMonitor<RetentionOptions>(
                        sp.GetRequiredService<IOptions<RetentionOptions>>().Value)));
                // And for the clarity thresholds, so /api/config/media can be shown to re-read
                // configuration rather than to have captured it once at startup.
                services.Replace(ServiceDescriptor.Singleton<IOptionsMonitor<ClarityOptions>>(sp =>
                    new MutableOptionsMonitor<ClarityOptions>(
                        sp.GetRequiredService<IOptions<ClarityOptions>>().Value)));
                // And §8's push settings, so a test can read the subscription cap instead of
                // hardcoding it (slice 3.4).
                services.Replace(ServiceDescriptor.Singleton<IOptionsMonitor<PushOptions>>(sp =>
                    new MutableOptionsMonitor<PushOptions>(
                        sp.GetRequiredService<IOptions<PushOptions>>().Value)));
                // And §5.3's broker settings, so the kill-switch can be thrown mid-test (slice 5.2).
                services.Replace(ServiceDescriptor.Singleton<IOptionsMonitor<BrokerOptions>>(sp =>
                    new MutableOptionsMonitor<BrokerOptions>(
                        sp.GetRequiredService<IOptions<BrokerOptions>>().Value)));
                // §5.3's upload-versus-submit race has no seam over HTTP: TestServer materialises a
                // request body before dispatching it, so a gated multipart part stalls the client and
                // never the server (measured in slice 7.1 — see GatingBlobStore). The stall goes
                // where the window actually is instead, between the handler's read of
                // `broker_request.state` and its SaveChanges. Pure delegation until armed.
                services.Replace(ServiceDescriptor.Singleton<IBlobStore>(sp =>
                    new GatingBlobStore(sp.GetRequiredService<InMemoryBlobStore>())));
                services.AddSingleton<IStartupFilter, RemoteIpTestFilter>();
            }));
        _ = Factory.Server; // boot now so the admin seeder has run before any test
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await using (var db = CreateDbContext())
        {
            await db.Database.EnsureDeletedAsync();
        }

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", null);
        Environment.SetEnvironmentVariable("Outbox__WorkerEnabled", null);
        Environment.SetEnvironmentVariable("Retention__CleanupEnabled", null);
    }
}
