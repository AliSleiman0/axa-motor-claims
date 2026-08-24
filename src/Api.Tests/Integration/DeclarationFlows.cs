using System.Net.Http.Json;
using Api.Modules.Declarations;
using Api.Modules.Media;
using Api.Modules.Users;
using Api.Tests.Media;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests.Integration;

/// <summary>A garage or officer user with a logged-in client — the <c>MappedExpert</c> shape.</summary>
internal sealed record Actor(AppUser User, HttpClient Client) : IDisposable
{
    public void Dispose() => Client.Dispose();
}

/// <summary>Mirror of the API's DeclarationListItemDto.</summary>
internal sealed record DeclarationListBodyDto(
    Guid Id,
    string State,
    string PlateNo,
    string? InsuredName,
    string? VisaNo,
    DateTime CreatedAt,
    DateTime? SubmittedAt,
    DateTime? DecidedAt,
    int MediaCount);

/// <summary>Mirror of the API's DeclarationDetailDto (G3).</summary>
internal sealed record DeclarationDetailBodyDto(
    Guid Id,
    string State,
    string PlateNo,
    string? InsuredName,
    string? Note,
    string? VisaNo,
    DateTime CreatedAt,
    DateTime? SubmittedAt,
    DateTime? DecidedAt,
    DateTime? RepairsStartedAt,
    DateTime? RepairDocsSubmittedAt,
    string? ClaimStatus,
    DateTime? ClaimFetchedAt,
    ClaimBodyDto? Claim,
    IReadOnlyList<CommentBodyDto> Comments);

internal sealed record CommentBodyDto(string Body, DateTime CreatedAt);

/// <summary>Mirror of the API's OfficerDeclarationListItemDto (O1).</summary>
internal sealed record OfficerDeclarationListBodyDto(
    Guid Id,
    string State,
    string PlateNo,
    string? InsuredName,
    string? GarageName,
    string? GarageEmail,
    string? GaragePhone,
    DateTime CreatedAt,
    DateTime? SubmittedAt,
    int MediaCount);

/// <summary>Mirror of the API's ClaimSearchResultDto.</summary>
internal sealed record ClaimSearchBodyDto(string VisaNo, string PlateNo, string InsuredName);

internal static class DeclarationFlows
{
    private static int _counter;

    /// <summary>A plate no other test uses — the suite shares one database for the whole run.</summary>
    public static string NextPlate() => $"PLC-D{Interlocked.Increment(ref _counter):D5}";

    public static async Task<Actor> CreateGarage(this ApiFixture fixture, string? email = null)
    {
        var user = await fixture.CreateUser(UserRole.Garage, UserStatus.Active);

        await using (var db = fixture.CreateDbContext())
        {
            db.GarageProfiles.Add(new GarageProfile
            {
                UserId = user.Id,
                ContactName = "PLACEHOLDER Garage Contact",
                Email = email ?? $"PLACEHOLDER-{user.Id:N}@example.invalid",
                Mobile = "+999000008001",
                Active = true,
            });
            await db.SaveChangesAsync();
        }

        return new Actor(user, await fixture.LoggedInClient(user));
    }

    /// <param name="withProfile">
    /// False stands up an officer with no profile row — the case the submit fan-out's left join
    /// exists for, where there is a device to push to and no address to fall back to.
    /// </param>
    public static async Task<Actor> CreateOfficer(
        this ApiFixture fixture, string? email = null, bool withProfile = true, bool active = true)
    {
        var user = await fixture.CreateUser(
            UserRole.ClaimOfficer, active ? UserStatus.Active : UserStatus.Inactive);

        if (withProfile)
        {
            await using var db = fixture.CreateDbContext();
            db.ClaimOfficerProfiles.Add(new ClaimOfficerProfile
            {
                UserId = user.Id,
                Next3User = $"PLACEHOLDER-OFF-{user.Id:N}"[..40],
                Email = email ?? $"PLACEHOLDER-{user.Id:N}@example.invalid",
            });
            await db.SaveChangesAsync();
        }

        // An inactive user cannot log in, so there is no client to hand back for one — the caller only
        // ever wants to prove they were *not* notified.
        return new Actor(user, active ? await fixture.LoggedInClient(user) : fixture.CreateClient());
    }

    private static async Task<HttpClient> LoggedInClient(this ApiFixture fixture, AppUser user)
    {
        var client = fixture.CreateClient();
        client.WithBearer((await fixture.Login(client, user.Phone)).AccessToken);
        return client;
    }

    public static async Task<Guid> CreateDraft(
        this Actor garage, string? plateNo = null, string? insuredName = null, string? note = null)
    {
        var response = await garage.Client.PostAsJsonAsync(
            "/api/garage/declarations",
            new { plateNo = plateNo ?? NextPlate(), insuredName, note });

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DeclarationListBodyDto>())!.Id;
    }

    public static string DeclarationDocumentsPath(Guid declarationId) =>
        $"/api/garage/declarations/{declarationId}/documents";

    /// <summary>A garage's supporting document — upload-allowed, so a picked PDF is legitimate.</summary>
    public static Task<HttpResponseMessage> UploadSurvey(
        this Actor garage, Guid declarationId, string fileName = "PLACEHOLDER-survey.pdf") =>
        MediaFlows.Upload(
            garage.Client,
            DeclarationDocumentsPath(declarationId),
            MediaFlows.Multipart(
                MediaBuckets.GarageDocuments, DocumentOrigins.Uploaded, TestImages.Pdf(4_000),
                ImageHeader.Pdf, fileName));

    /// <summary>A garage's car photo — capture-only, the BRD's hard rule.</summary>
    public static Task<HttpResponseMessage> UploadCarPhoto(this Actor garage, Guid declarationId) =>
        MediaFlows.Upload(
            garage.Client,
            DeclarationDocumentsPath(declarationId),
            MediaFlows.Multipart(
                MediaBuckets.GarageCarPhoto, DocumentOrigins.Captured, TestImages.Jpeg(1600, 1200)));

    /// <summary>#18's approval PNG, which slice 4.2's browser renders and only an officer may post.</summary>
    public static Task<HttpResponseMessage> UploadApprovalImage(this Actor officer, Guid declarationId) =>
        MediaFlows.Upload(
            officer.Client,
            $"/api/officer/declarations/{declarationId}/documents",
            MediaFlows.Multipart(
                MediaBuckets.ApprovalImage, DocumentOrigins.Captured, TestImages.Png(1600, 1200),
                ImageHeader.Png, "PLACEHOLDER-approval.png"));

    public static Task<HttpResponseMessage> Submit(this Actor garage, Guid declarationId) =>
        garage.Client.PostAsync(
            new Uri($"/api/garage/declarations/{declarationId}/submit", UriKind.Relative), null);

    /// <summary>G4's post-repair photo — capture-only, the same BRD rule as the declaration's.</summary>
    public static Task<HttpResponseMessage> UploadRepairPhoto(this Actor garage, Guid declarationId) =>
        MediaFlows.Upload(
            garage.Client,
            DeclarationDocumentsPath(declarationId),
            MediaFlows.Multipart(
                MediaBuckets.RepairPhoto, DocumentOrigins.Captured, TestImages.Jpeg(1600, 1200)));

    /// <summary>G4's discharge — paperwork, so a picked PDF is legitimate.</summary>
    public static Task<HttpResponseMessage> UploadDischarge(
        this Actor garage, Guid declarationId, string fileName = "PLACEHOLDER-discharge.pdf") =>
        MediaFlows.Upload(
            garage.Client,
            DeclarationDocumentsPath(declarationId),
            MediaFlows.Multipart(
                MediaBuckets.Discharge, DocumentOrigins.Uploaded, TestImages.Pdf(4_000),
                ImageHeader.Pdf, fileName));

    /// <summary>G4's invoice — the one AXA settles against, and still not required on its own.</summary>
    public static Task<HttpResponseMessage> UploadInvoice(
        this Actor garage, Guid declarationId, string fileName = "PLACEHOLDER-invoice.pdf") =>
        MediaFlows.Upload(
            garage.Client,
            DeclarationDocumentsPath(declarationId),
            MediaFlows.Multipart(
                MediaBuckets.Invoice, DocumentOrigins.Uploaded, TestImages.Pdf(4_000),
                ImageHeader.Pdf, fileName));

    public static Task<HttpResponseMessage> StartRepairs(this Actor garage, Guid declarationId) =>
        garage.Client.PostAsync(
            new Uri($"/api/garage/declarations/{declarationId}/start-repairs", UriKind.Relative), null);

    public static Task<HttpResponseMessage> Approve(
        this Actor officer, Guid declarationId, string visaNo, string? comment = null) =>
        officer.Client.PostAsJsonAsync(
            $"/api/officer/declarations/{declarationId}/approve", new { visaNo, comment });

    public static Task<HttpResponseMessage> Reject(
        this Actor officer, Guid declarationId, string? comment = null) =>
        officer.Client.PostAsJsonAsync(
            $"/api/officer/declarations/{declarationId}/reject", new { comment });

    public static Task<HttpResponseMessage> SubmitRepairDocs(this Actor garage, Guid declarationId) =>
        garage.Client.PostAsync(
            new Uri($"/api/garage/declarations/{declarationId}/submit-repair-docs", UriKind.Relative), null);

    /// <summary>Takes a declaration all the way to submitted, with one document attached.</summary>
    public static async Task<Guid> SubmittedDeclaration(this Actor garage, string? plateNo = null)
    {
        var id = await garage.CreateDraft(plateNo);
        (await garage.UploadCarPhoto(id)).EnsureSuccessStatusCode();
        (await garage.Submit(id)).EnsureSuccessStatusCode();
        return id;
    }

    /// <summary>
    /// The whole §5.2 chain up to the state G4 starts from: draft → car photo → submit → approval
    /// image → approve → start repairs. The visa is seeded in the fake so the approve call's NEXT3
    /// verification finds it.
    /// </summary>
    public static async Task<Guid> RepairingDeclaration(
        this ApiFixture fixture, Actor garage, Actor officer, string visaNo)
    {
        fixture.SeedClaim(visaNo);

        var id = await garage.SubmittedDeclaration();
        (await officer.UploadApprovalImage(id)).EnsureSuccessStatusCode();
        (await officer.Approve(id, visaNo)).EnsureSuccessStatusCode();
        (await garage.StartRepairs(id)).EnsureSuccessStatusCode();

        return id;
    }

    public static async Task<Declaration> DeclarationRow(this ApiFixture fixture, Guid declarationId)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Declarations.AsNoTracking().SingleAsync(d => d.Id == declarationId);
    }

    public static async Task<List<Document>> DocumentRows(this ApiFixture fixture, Guid declarationId)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Documents.AsNoTracking()
            .Where(d => d.OwnerKind == DocumentOwnerKinds.Declaration && d.OwnerId == declarationId)
            .OrderBy(d => d.CreatedAt)
            .ToListAsync();
    }

    public static async Task<int> CommentCount(this ApiFixture fixture, Guid declarationId)
    {
        await using var db = fixture.CreateDbContext();
        return await db.DeclarationComments.CountAsync(c => c.DeclarationId == declarationId);
    }
}
