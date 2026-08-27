namespace Api.Infrastructure;

/// <summary>
/// The security response headers every reply carries, added in slice 7.1 because the API carried
/// none at all.
/// </summary>
/// <remarks>
/// <para>
/// **Registered first in the pipeline**, which is the whole design. The two things most worth
/// covering are the two that never reach a handler: the rate limiter writes its own 429 and
/// <see cref="Modules.PublicSurface.PublicBodySizeMiddleware"/> its own 413, both short-circuiting
/// everything downstream. A header applied anywhere later would be absent from exactly the responses
/// an abusive caller sees most of.
/// </para>
/// <para>
/// **Set from <c>Response.OnStarting</c> rather than inline**, so the assignment happens after every
/// handler has written its own headers. That is what lets <c>DocumentContent</c> keep its own
/// <c>nosniff</c> line — the two writers agree on one value and the header stays single-valued, which
/// two existing tests assert with <c>Single()</c>. Assignment, never <c>Append</c>.
/// </para>
/// <para>
/// **No configuration knob.** Every value here is either right everywhere or wrong everywhere, and a
/// switch would only be a way to deploy the wrong one. In particular <c>Strict-Transport-Security</c>
/// is unconditional: a browser ignores an HSTS header received over plain <c>http</c>, so local
/// development is unaffected and there is nothing for an environment to decide.
/// </para>
/// <para>
/// The CSP is the strictest one that fits what this API serves today — JSON, and document bytes the
/// browser fetches through the authorized client and renders from a <c>blob:</c> URL (slice 4.2), so
/// nothing depends on a directly-navigated response being allowed to load subresources.
/// **Slice 7.3 carves out HTML** when the SPA moves in behind the same origin; its card says so.
/// </para>
/// <para>
/// **Two notes for whoever writes that carve-out** (both from 7.1's <c>/security-review</c>). Kestrel
/// runs <c>OnStarting</c> callbacks **last-registered-first**, so this one — registered before
/// everything — runs *after* any callback a later middleware or endpoint adds, and would silently
/// overwrite it. The carve-out therefore belongs **inside <see cref="Apply"/>**, keyed on the
/// response's content type, not in a second callback further down the pipeline. And
/// <see cref="Apply"/> assigns rather than merges, which is right for the one overlap that exists
/// today (<c>DocumentContent</c>'s identical <c>nosniff</c>) but means a future handler setting its
/// own <c>Content-Security-Policy</c> inline would have it discarded with nothing going red.
/// </para>
/// <para>
/// **What the policy deliberately does not carry yet.** <c>form-action</c> and <c>base-uri</c> do not
/// fall back to <c>default-src</c>, so <c>'none'</c> here does not cover them — but neither directive
/// bites on anything but an HTML document, and this API serves none until 7.3. They belong to the
/// same card as the carve-out. <c>Strict-Transport-Security</c> likewise carries no
/// <c>includeSubDomains</c>: §10 describes a single host, and asserting a policy over sibling
/// subdomains AXA may already be using is a deployment decision rather than a code one.
/// </para>
/// </remarks>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Response.OnStarting(
            static state =>
            {
                Apply(((HttpContext)state).Response.Headers);
                return Task.CompletedTask;
            },
            context);

        return next(context);
    }

    private static void Apply(IHeaderDictionary headers)
    {
        // The response means what it says it means: no sniffing a JSON error body into HTML, and no
        // sniffing a customer's uploaded file into a script.
        headers.XContentTypeOptions = "nosniff";

        // Clickjacking, twice over. `X-Frame-Options` for what still only understands that, and the
        // CSP directive beside it for everything current.
        headers.XFrameOptions = "DENY";
        headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";

        // §9.1's public page carries a live 256-bit credential in its URL. A referrer header would
        // hand that token to any third-party host the page ever touched.
        headers["Referrer-Policy"] = "no-referrer";

        headers.StrictTransportSecurity = "max-age=31536000";
    }
}
