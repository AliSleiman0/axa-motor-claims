using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Api.Tests.Integration;

/// <summary>
/// TestServer leaves <c>Connection.RemoteIpAddress</c> null, which would make every request in the
/// suite share one rate-limit partition and leave §9.1's per-IP limit untestable. This filter lets
/// a test say which IP it is coming from.
/// </summary>
/// <remarks>
/// Test-only and inert unless <c>X-Test-Ip</c> is present, so no production code path is altered
/// and no other test's behaviour changes.
/// </remarks>
public sealed class RemoteIpTestFilter : IStartupFilter
{
    public const string HeaderName = "X-Test-Ip";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return app =>
        {
            app.Use(async (context, continuation) =>
            {
                if (context.Request.Headers.TryGetValue(HeaderName, out var value)
                    && IPAddress.TryParse(value.ToString(), out var address))
                {
                    context.Connection.RemoteIpAddress = address;
                }

                await continuation();
            });

            next(app);
        };
    }
}
