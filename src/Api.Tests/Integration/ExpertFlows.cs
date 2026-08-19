using System.Net.Http.Json;
using Api.Integrations;
using Api.Integrations.Next3;
using Api.Modules.Users;
using Microsoft.Extensions.DependencyInjection;

namespace Api.Tests.Integration;

internal sealed record AssignmentListItemDto(
    Guid Id,
    string VisaNo,
    DateTime ReceivedAt,
    DateTime? OpenedAt,
    DateTime? ArrivedAt,
    string? PlateNo,
    string? InsuredName,
    string? CarMakeModel,
    DateOnly? AccidentDate);

internal sealed record ClaimBodyDto(
    string VisaNo,
    string PolicyNo,
    string PlateNo,
    string InsuredName,
    string InsuredPhone,
    string CarMakeModel,
    string City,
    DateOnly AccidentDate);

internal sealed record AssignmentDetailDto(
    Guid Id,
    string VisaNo,
    DateTime ReceivedAt,
    DateTime? OpenedAt,
    DateTime? ArrivedAt,
    string ClaimStatus,
    DateTime? ClaimFetchedAt,
    ClaimBodyDto? Claim);

/// <summary>An expert user, their NEXT3 id, and a logged-in client for them.</summary>
internal sealed record MappedExpert(AppUser User, string Next3Id, HttpClient Client) : IDisposable
{
    public void Dispose() => Client.Dispose();
}

internal static class ExpertFlows
{
    private static int _counter;

    /// <summary>
    /// The whole suite shares one database for the run, so every scenario needs values no other test
    /// uses — the visa/ref equivalent of <see cref="TestPhones.Next"/>.
    /// </summary>
    public static string NextVisa() => $"PLACEHOLDER-VISA-T{Interlocked.Increment(ref _counter):D5}";

    public static string NextRef() => $"PLACEHOLDER-ASG-T{Interlocked.Increment(ref _counter):D5}";

    public static string NextExpertNext3Id() => $"PLACEHOLDER-EXP-T{Interlocked.Increment(ref _counter):D5}";

    /// <summary>
    /// An active expert whose profile carries a NEXT3 id — the mapping the assignment handler
    /// resolves against (§4 expert_profile). Created directly: the admin CRUD path is slice 1.3's
    /// to test, and this one only needs the row to exist.
    /// </summary>
    public static async Task<MappedExpert> CreateMappedExpert(
        this ApiFixture fixture, string? next3Id = null, bool profileActive = true)
    {
        var user = await fixture.CreateUser(UserRole.Expert, UserStatus.Active);
        var id = next3Id ?? NextExpertNext3Id();

        await using (var db = fixture.CreateDbContext())
        {
            db.ExpertProfiles.Add(new ExpertProfile
            {
                UserId = user.Id,
                Next3Id = id,
                Email = $"PLACEHOLDER-{user.Id:N}@example.invalid",
                Active = profileActive,
            });
            await db.SaveChangesAsync();
        }

        var client = fixture.CreateClient();
        client.WithBearer((await fixture.Login(client, user.Phone)).AccessToken);
        return new MappedExpert(user, id, client);
    }

    /// <summary>Seeds a claim into the fake NEXT3 so a scenario has something to cache.</summary>
    public static string SeedClaim(this ApiFixture fixture, string? visaNo = null, string? insuredName = null)
    {
        var visa = visaNo ?? NextVisa();
        fixture.Services.GetRequiredService<FakeNext3Client>().Seed(new ClaimDetail(
            visa,
            "PLACEHOLDER-POL-T01",
            "PLC-TEST-T1",
            insuredName ?? "PLACEHOLDER Insured T",
            "+999000009001",
            "PLACEHOLDER Make T",
            "PLACEHOLDER City T",
            new DateOnly(2026, 8, 12)));
        return visa;
    }

    /// <summary>
    /// Delivers an assignment the way NEXT3 would — through <see cref="IAssignmentSource"/> and the
    /// handler subscribed at startup. This is the production wiring, not a shortcut past it.
    /// </summary>
    public static Task Inject(this ApiFixture fixture, string visaNo, string expertNext3Id, string assignmentRef) =>
        fixture.Services.GetRequiredService<FakeAssignmentSource>()
            .Inject(new AssignmentReceived(visaNo, expertNext3Id, assignmentRef), CancellationToken.None);

    /// <summary>The admin dev endpoint (§6.2's demo trigger).</summary>
    public static Task<HttpResponseMessage> PostInjection(
        HttpClient admin, string? visaNo, string? expertNext3Id, string? assignmentRef) =>
        admin.PostAsJsonAsync(
            "/api/admin/dev/assignments",
            new { visaNo, expertNext3Id, next3AssignmentRef = assignmentRef });

    /// <summary>
    /// Runs a scenario with the fake world broken, then restores it. Everything shares one
    /// FakeBehavior and the collection is serialized, so a leaked failure rate breaks later tests.
    /// </summary>
    public static async Task<T> WithNext3Down<T>(this ApiFixture fixture, Func<Task<T>> scenario)
    {
        var original = fixture.Fake.CurrentValue;
        fixture.Fake.CurrentValue = new FakeOptions { FailureRate = 1, LatencyMs = original.LatencyMs };
        try
        {
            return await scenario();
        }
        finally
        {
            fixture.Fake.CurrentValue = original;
        }
    }
}
