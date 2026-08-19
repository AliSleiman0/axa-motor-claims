using System.Globalization;
using System.Security.Claims;
using Api.Infrastructure;
using Api.Modules.Audit;
using Microsoft.EntityFrameworkCore;

namespace Api.Modules.Users;

public sealed record OtpRequestDto(string Phone);

public sealed record OtpVerifyDto(string Phone, string Code);

public sealed record RefreshDto(string RefreshToken);

public sealed record InviteAcceptDto(string Token);

public sealed record InviteVerifyDto(string Token, string Code);

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth");

        group.MapPost("/otp/request", async (OtpRequestDto dto, OtpService otp, HttpContext http, CancellationToken ct) =>
        {
            if (!Phone.IsValidE164(dto.Phone))
            {
                return Results.BadRequest();
            }

            var result = await otp.RequestLoginCode(dto.Phone, ct);
            return result.IsThrottled ? TooManyRequests(http, result.RetryAfterSeconds) : Results.Ok();
        });

        group.MapPost("/otp/verify", async (
            OtpVerifyDto dto, OtpService otp, TokenService tokens, AppDbContext db, AuditWriter audit,
            CancellationToken ct) =>
        {
            // Uniform 401: wrong, expired, consumed, locked-out, and unknown-phone are indistinguishable
            // to the client; the audit detail keeps the reasons apart (§9).
            if (!await otp.VerifyCode(dto.Phone, dto.Code, ct))
            {
                audit.Append(null, AuditActions.LoginFailed, AuditEntityKinds.AppUser, null,
                    new { dto.Phone, Reason = "code_rejected" });
                await db.SaveChangesAsync(ct);
                return Results.Unauthorized();
            }

            var user = await db.Users
                .SingleOrDefaultAsync(u => u.Phone == dto.Phone && u.Status == UserStatus.Active, ct);
            if (user is null)
            {
                audit.Append(null, AuditActions.LoginFailed, AuditEntityKinds.AppUser, null,
                    new { dto.Phone, Reason = "no_active_user" });
                await db.SaveChangesAsync(ct);
                return Results.Unauthorized();
            }

            // IssueTokens saves — the audit row and the refresh-token row commit together.
            audit.Append(user.Id, AuditActions.LoginSucceeded, AuditEntityKinds.AppUser, user.Id);
            return Results.Ok(await tokens.IssueTokens(user, ct));
        });

        group.MapPost("/refresh", async (RefreshDto dto, TokenService tokens, CancellationToken ct) =>
        {
            var pair = await tokens.Rotate(dto.RefreshToken, ct);
            return pair is null ? Results.Unauthorized() : Results.Ok(pair);
        });

        group.MapPost("/invite/accept", async (
            InviteAcceptDto dto, InviteService invites, HttpContext http, CancellationToken ct) =>
        {
            var result = await invites.Accept(dto.Token, ct);
            return result.Outcome switch
            {
                InviteAcceptOutcome.OtpSent => Results.Ok(),
                InviteAcceptOutcome.Throttled => TooManyRequests(http, result.RetryAfterSeconds),
                // Uniform bare 404: invalid, expired, and used tokens are indistinguishable.
                _ => Results.NotFound(),
            };
        });

        group.MapPost("/invite/verify", async (InviteVerifyDto dto, InviteService invites, CancellationToken ct) =>
        {
            var result = await invites.VerifyAndActivate(dto.Token, dto.Code, ct);
            return result.Outcome switch
            {
                InviteVerifyOutcome.Activated => Results.Ok(result.Tokens),
                InviteVerifyOutcome.CodeRejected => Results.Unauthorized(),
                _ => Results.NotFound(),
            };
        });

        group.MapGet("/me", async (ClaimsPrincipal principal, AppDbContext db, CancellationToken ct) =>
        {
            var userId = principal.GetUserId();
            if (userId is null)
            {
                return Results.Unauthorized();
            }

            var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, ct);
            return user is null
                ? Results.Unauthorized()
                : Results.Ok(new { user.Id, user.Phone, Role = user.Role.ToDbValue(), user.DisplayName });
        }).RequireAuthorization(AuthPolicies.ActiveUser);

        return app;
    }

    private static IResult TooManyRequests(HttpContext http, int retryAfterSeconds)
    {
        http.Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        return Results.StatusCode(StatusCodes.Status429TooManyRequests);
    }
}
