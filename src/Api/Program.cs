using Api.Composition;
using Api.Modules.Broker;
using Api.Modules.Expert;
using Api.Modules.Media;
using Api.Modules.PublicSurface;
using Api.Modules.Users;

var builder = WebApplication.CreateBuilder(args);

// Placeholders load after appsettings*.json; env vars are re-added last so real values
// always override placeholders without code changes (design.md Appendix A).
builder.Configuration.AddJsonFile("appsettings.Placeholders.json", optional: false, reloadOnChange: true);
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
app.MapExpertEndpoints();
app.MapExpertDocumentEndpoints();
app.MapDevAssignmentEndpoints();
app.MapBrokerLinkEndpoints();
app.MapPublicEndpoints();

await AdminSeeder.Seed(app.Services);

app.Run();
