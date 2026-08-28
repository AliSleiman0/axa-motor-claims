using Api.Composition;
using Api.Infrastructure;
using Api.Modules.Broker;
using Api.Modules.Declarations;
using Api.Modules.Expert;
using Api.Modules.Media;
using Api.Modules.PublicSurface;
using Api.Modules.Push;
using Api.Modules.Users;
using Api.Outbox;

var builder = WebApplication.CreateBuilder(args);

// Placeholders load after appsettings*.json, then everything that carries a *real* value is re-added
// on top, so a real setting always beats a placeholder without a code change (design.md Appendix A).
builder.Configuration.AddJsonFile("appsettings.Placeholders.json", optional: false, reloadOnChange: true);

// **User secrets have to be re-added here, and the omission was a live bug** (found in slice 3.4's
// browser pass, invisible to the whole suite). CreateBuilder already added them — but *before* the
// line above, so the placeholder file silently overrode every one of them. Anything set with
// `dotnet user-secrets` was accepted, stored, and then ignored: the app booted on PLACEHOLDER values
// while the developer had every reason to believe otherwise. That is not push-specific — it applied
// equally to `Auth:Jwt:SigningKey` and to NEXT3's credentials once #1 lands.
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>(optional: true);
}

builder.Configuration.AddEnvironmentVariables();

// **The global request-body ceiling, which did not exist before slice 7.2.** Kestrel's default is
// 30 MB, so an authenticated caller could push a 29 MB file all the way through TLS, the pipeline and
// the multipart reader before `Media:MaxFileMb` refused it — paying for every byte of a request that
// was never going to be accepted. One megabyte of headroom above the per-file cap covers the
// multipart envelope (boundaries, the two metadata parts, `Content-Disposition` headers) so a legal
// 15 MB file is never refused by the wrong limit with the wrong error.
//
// **Read once, at startup, and deliberately not reloadable.** Everything else in this app reads
// options through `IOptionsMonitor` so `appsettings.Placeholders.json` can be edited live (§11's
// week-4 demo depends on it); a Kestrel limit is fixed when the server is built, so a monitor here
// would be a promise the server cannot keep. Raising `Media:MaxFileMb` therefore needs a restart.
//
// `/public/*` still lowers it per request through `PublicBodySizeMiddleware` and
// `IHttpMaxRequestBodySizeFeature` — that feature can only lower the effective limit, never raise it,
// so §9.1's much tighter `PublicLink:MaxFileMb` is unaffected by whatever this line says.
builder.WebHost.ConfigureKestrel((context, kestrel) =>
{
    var maxFileMb = context.Configuration.GetValue<int?>("Media:MaxFileMb") ?? new MediaOptions().MaxFileMb;
    kestrel.Limits.MaxRequestBodySize = (maxFileMb + 1L) * 1024 * 1024;
});

builder.Services.AddAxaMotorClaims(builder.Configuration);

var app = builder.Build();

// Outermost, so it wraps everything below including the limiter (slice 7.2). A client that has gone
// away turns an ordinary abort into an unhandled exception and a 503 in the log; the guard is
// `RequestAborted`, never the exception's type. Deliberately not a global handler — with a live
// client the exception propagates exactly as it did before, which §9.1's uniform surface relies on.
app.UseMiddleware<CancelledRequestMiddleware>();

// First among everything that writes a response, and before the two middlewares that short-circuit:
// the limiter answers its own 429 and the body cap its own 413, so anything registered later would
// leave exactly those responses bare (§9).
app.UseMiddleware<SecurityHeadersMiddleware>();

// Before authentication: abuse of the public surface is shed before any work is done for it (§9.1).
app.UseRateLimiter();
app.UseWhen(
    http => http.Request.Path.StartsWithSegments(
        PublicRateLimiting.PathPrefix, StringComparison.OrdinalIgnoreCase),
    branch => branch.UseMiddleware<PublicBodySizeMiddleware>());

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapMediaConfigEndpoints();
app.MapAuthEndpoints();
app.MapRoleEndpoints();
app.MapAdminUserEndpoints();
app.MapAdminProfileEndpoints();
app.MapOutboxAdminEndpoints();
app.MapExpertEndpoints();
app.MapExpertDocumentEndpoints();
app.MapGarageDeclarationEndpoints();
app.MapOfficerEndpoints();
app.MapPushEndpoints();
app.MapDevAssignmentEndpoints();
app.MapBrokerLinkEndpoints();
app.MapBrokerRequestEndpoints();
app.MapPublicEndpoints();

await AdminSeeder.Seed(app.Services);

app.Run();
