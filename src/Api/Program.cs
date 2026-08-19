using Api.Composition;
using Api.Modules.Users;

var builder = WebApplication.CreateBuilder(args);

// Placeholders load after appsettings*.json; env vars are re-added last so real values
// always override placeholders without code changes (design.md Appendix A).
builder.Configuration.AddJsonFile("appsettings.Placeholders.json", optional: false, reloadOnChange: true);
builder.Configuration.AddEnvironmentVariables();

builder.Services.AddAxaMotorClaims(builder.Configuration);

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapAuthEndpoints();
app.MapRoleEndpoints();
app.MapAdminUserEndpoints();
app.MapAdminProfileEndpoints();

await AdminSeeder.Seed(app.Services);

app.Run();
