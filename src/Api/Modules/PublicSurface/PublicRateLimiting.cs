using System.Globalization;
using System.Threading.RateLimiting;
using Api.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Api.Modules.PublicSurface;

/// <summary>
/// design.md §9.1's "per-IP and per-token rate limits on every <c>/public/*</c> endpoint".
/// </summary>
/// <remarks>
/// <para>
/// Registered as the global limiter rather than an endpoint policy because §9.1 asks for two
/// independent limits at once: a chained limiter applies both, whereas a single policy could only
/// partition on an IP+token pair — which an attacker defeats by varying either half.
/// </para>
/// <para>
/// Everything outside <c>/public</c> passes through unlimited: the authenticated surface has its own
/// controls (OTP resend throttling, §9's authorization), and silently throttling an expert at a
/// crash site would be a far worse failure than the abuse this is defending against.
/// </para>
/// </remarks>
public static class PublicRateLimiting
{
    /// <summary>The one prefix this limiter guards; also the route prefix in <see cref="PublicEndpoints"/>.</summary>
    public const string PathPrefix = "/public";

    public static IServiceCollection AddPublicRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            limiter.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, string>(PartitionByIp),
                PartitionedRateLimiter.Create<HttpContext, string>(PartitionByToken));

            limiter.OnRejected = (context, ct) =>
            {
                // Matches the auth surface's 429 shape (AuthEndpoints.TooManyRequests).
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                return ValueTask.CompletedTask;
            };
        });

        return services;
    }

    private static RateLimitPartition<string> PartitionByIp(HttpContext http)
    {
        if (!IsPublicPath(http))
        {
            return RateLimitPartition.GetNoLimiter("non-public");
        }

        var options = Limits(http);
        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return FixedWindow($"ip:{ip}", options.PerIpPermitsPerMinute);
    }

    private static RateLimitPartition<string> PartitionByToken(HttpContext http)
    {
        if (!IsPublicPath(http))
        {
            return RateLimitPartition.GetNoLimiter("non-public");
        }

        var options = Limits(http);
        // Hashed, never raw: a partition key reaches logs, metrics and dumps, and the raw token is
        // a live credential. Hashing also means an invalid token throttles exactly like a valid one,
        // so the limiter cannot be used to probe which tokens exist.
        var token = ExtractToken(http.Request.Path);
        var key = token is null ? "none" : TokenHashing.Hash(token);
        return FixedWindow($"token:{key}", options.PerTokenPermitsPerMinute);
    }

    private static RateLimitPartition<string> FixedWindow(string key, int permitsPerMinute) =>
        RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitsPerMinute,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true,
        });

    private static bool IsPublicPath(HttpContext http) =>
        http.Request.Path.StartsWithSegments(PathPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Read per request, not captured at startup, so the limits are a live configuration knob
    /// (and so tests can lower them without booting a second host).
    /// </summary>
    private static PublicRateLimitOptions Limits(HttpContext http) =>
        http.RequestServices.GetRequiredService<IOptionsMonitor<PublicLinkOptions>>().CurrentValue.RateLimit;

    /// <summary>
    /// The token is the first segment after the prefix (<c>/public/{token}/...</c>). Parsed from the
    /// path rather than from route values so the limiter does not depend on running after routing.
    /// </summary>
    private static string? ExtractToken(PathString path)
    {
        if (!path.StartsWithSegments(PathPrefix, StringComparison.OrdinalIgnoreCase, out var remaining))
        {
            return null;
        }

        var segment = remaining.Value?.TrimStart('/');
        if (string.IsNullOrEmpty(segment))
        {
            return null;
        }

        var slash = segment.IndexOf('/', StringComparison.Ordinal);
        return slash < 0 ? segment : segment[..slash];
    }
}
