using Api.Infrastructure;
using Api.Integrations.Sms;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero));

    public IServiceProvider Services => Factory.Services;

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

        await using (var db = CreateDbContext())
        {
            await db.Database.MigrateAsync();
        }

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.Replace(ServiceDescriptor.Singleton<ISmsSender>(Sms));
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(Time));
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

        Environment.SetEnvironmentVariable("ConnectionStrings__Default", null);
    }
}
