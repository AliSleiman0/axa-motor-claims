using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace Api.Modules.PublicSurface;

/// <summary>
/// design.md §9.1's per-file size cap, applied to every <c>/public/*</c> request.
/// </summary>
/// <remarks>
/// <para>
/// Middleware rather than an endpoint filter, because a filter runs *after* model binding: by the
/// time it saw the request the body had already been read, and an oversized payload surfaced as a
/// malformed-JSON 400 instead of a cap rejection. The point of a cap is to not read the body.
/// </para>
/// <para>
/// Two layers: <c>Content-Length</c> is a claim by the caller and rejects the honest case cheaply;
/// lowering <see cref="IHttpMaxRequestBodySizeFeature"/> makes the server itself cut off a caller
/// who lies or streams chunked. Byte-accurate per-file counting arrives with the media pipeline in
/// slice 2.3 and calls <see cref="PublicUploadCaps"/>.
/// </para>
/// </remarks>
public sealed class PublicBodySizeMiddleware(RequestDelegate next, IOptionsMonitor<PublicLinkOptions> options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var cap = options.CurrentValue.MaxFileBytes;

        if (context.Request.ContentLength > cap)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }

        var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false })
        {
            feature.MaxRequestBodySize = cap;
        }

        await next(context);
    }
}
