using Api.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace Api.Tests.Unit;

/// <summary>
/// The HTML/non-HTML CSP branch added slice 7.6 (design.md §10). Unit-tested on a constructed
/// <see cref="DefaultHttpContext"/> rather than through a routed response: no test host has a
/// `wwwroot`, so there is no real HTML response to send through the pipeline — the same accepted
/// gap the static-file-serving fallback itself carries (covered by the deploy smoke instead).
/// </summary>
public class SecurityHeadersMiddlewareTests
{
    [Fact]
    public void NonHtmlResponse_KeepsTheFullStrictPolicy()
    {
        var context = new DefaultHttpContext();
        context.Response.ContentType = "application/json";

        SecurityHeadersMiddleware.Apply(context);

        Assert.Equal("default-src 'none'; frame-ancestors 'none'", context.Response.Headers.ContentSecurityPolicy);
    }

    [Fact]
    public void HtmlResponse_KeepsOnlyTheClickjackingDirective()
    {
        var context = new DefaultHttpContext();
        context.Response.ContentType = "text/html; charset=utf-8";

        SecurityHeadersMiddleware.Apply(context);

        Assert.Equal("frame-ancestors 'none'", context.Response.Headers.ContentSecurityPolicy);
    }

    [Fact]
    public void NoContentType_KeepsTheFullStrictPolicy()
    {
        // The default for every existing response (JSON bodies set it via the framework, but an
        // empty 401/404 may carry none at all) — must not be misread as HTML.
        var context = new DefaultHttpContext();

        SecurityHeadersMiddleware.Apply(context);

        Assert.Equal("default-src 'none'; frame-ancestors 'none'", context.Response.Headers.ContentSecurityPolicy);
    }

    [Fact]
    public void EveryResponse_StillCarriesTheOtherHeadersUnchanged()
    {
        var context = new DefaultHttpContext();
        context.Response.ContentType = "text/html";

        SecurityHeadersMiddleware.Apply(context);

        Assert.Equal("nosniff", context.Response.Headers.XContentTypeOptions);
        Assert.Equal("DENY", context.Response.Headers.XFrameOptions);
        Assert.Equal("no-referrer", context.Response.Headers["Referrer-Policy"]);
        Assert.Equal("max-age=31536000", context.Response.Headers.StrictTransportSecurity);
    }
}
