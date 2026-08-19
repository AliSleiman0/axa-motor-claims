using System.Net.Http.Headers;
using System.Net.Http.Json;
using Api.Modules.Users;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Api.Tests.Integration;

internal static class TestPhones
{
    private static int _counter;

    public static string Next() => $"+99912{Interlocked.Increment(ref _counter):D7}";
}

internal sealed record TokenPairDto(string AccessToken, string RefreshToken);

internal sealed record MeDto(Guid Id, string Phone, string Role, string DisplayName);

internal static class AuthFlows
{
    public static async Task<AppUser> CreateUser(
        this ApiFixture fixture, UserRole role, UserStatus status, string? phone = null)
    {
        var user = new AppUser
        {
            Id = Guid.CreateVersion7(),
            Phone = phone ?? TestPhones.Next(),
            Role = role,
            DisplayName = $"Test {role}",
            Status = status,
            CreatedAt = fixture.Time.GetUtcNow().UtcDateTime,
        };
        await using var db = fixture.CreateDbContext();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    /// <summary>Arranges an invite through DI — invite issuance has no endpoint until slice 1.3's A1 CRUD.</summary>
    public static async Task<string> IssueInvite(this ApiFixture fixture, Guid userId)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var invites = scope.ServiceProvider.GetRequiredService<InviteService>();
        return await invites.Issue(userId, CancellationToken.None);
    }

    public static async Task<TokenPairDto> Login(this ApiFixture fixture, HttpClient client, string phone)
    {
        // Skip past any resend throttle left by earlier steps for this phone.
        fixture.Time.Advance(TimeSpan.FromSeconds(61));
        var request = await client.PostAsJsonAsync("/auth/otp/request", new { phone });
        request.EnsureSuccessStatusCode();
        var code = fixture.Sms.LastOtpFor(phone);
        var verify = await client.PostAsJsonAsync("/auth/otp/verify", new { phone, code });
        verify.EnsureSuccessStatusCode();
        return (await verify.Content.ReadFromJsonAsync<TokenPairDto>())!;
    }

    public static AuthOptions AuthOptions(this ApiFixture fixture) =>
        fixture.Services.GetRequiredService<IOptions<AuthOptions>>().Value;

    public static HttpClient WithBearer(this HttpClient client, string accessToken)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }
}
