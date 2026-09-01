using Api.Integrations.Blob;
using Microsoft.Extensions.Options;

namespace Api.Infrastructure;

/// <summary>
/// design.md §10 (slice 7.6). Replaces the one-line inline `/health` that existed before this
/// slice with two distinct answers Container Apps' two different probes actually need.
///
/// **Liveness (`/health`) stays a dumb 200, unconditionally** — restarting a container because a
/// downstream dependency is briefly unavailable is the wrong response to that fact; that is what
/// readiness gates traffic on instead.
///
/// **Readiness (`/health/ready`) is hand-rolled rather than a package** — two checks (a DB round
/// trip, and a blob container check only when `Blob:Mode = azure`) do not earn
/// `Microsoft.Extensions.Diagnostics.HealthChecks`' ceremony. Both checks are wrapped so a thrown
/// exception (an unreachable database, a storage client whose lazily-created container reference
/// cached a startup failure — see `AzureBlobStore`) counts as "not ready" rather than crashing the
/// endpoint itself; a health check that can 500 is not a health check.
/// </summary>
public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        app.MapGet("/health/ready", async (
            AppDbContext db, IOptions<BlobOptions> blobOptions, IBlobStore blobStore, CancellationToken ct) =>
        {
            var dbOk = await TryCheck(() => db.Database.CanConnectAsync(ct));

            // Only when it means something: fake mode has no container, and asking would just be
            // exercising InMemoryBlobStore's own unconditional `true` rather than checking anything.
            var blobOk = string.Equals(blobOptions.Value.Mode, "azure", StringComparison.Ordinal)
                ? await TryCheck(() => blobStore.ContainerExists(ct))
                : true;

            var checks = new { db = dbOk, blob = blobOk };
            return dbOk && blobOk
                ? Results.Ok(new { status = "ok", checks })
                : Results.Json(new { status = "unavailable", checks }, statusCode: StatusCodes.Status503ServiceUnavailable);
        });

        return app;
    }

    private static async Task<bool> TryCheck(Func<Task<bool>> check)
    {
        try
        {
            return await check();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }
}
