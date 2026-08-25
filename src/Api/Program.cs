using Api.Composition;
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

builder.Services.AddAxaMotorClaims(builder.Configuration);

var app = builder.Build();

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
