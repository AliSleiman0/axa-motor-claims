using System.Net.Http.Headers;
using Api.Infrastructure.Cleanup;
using Api.Modules.Media;
using Api.Outbox;
using Api.Tests.Media;
using Microsoft.EntityFrameworkCore;

namespace Api.Tests.Integration;

/// <summary>Mirror of the API's DocumentDto, for deserializing a 201 body.</summary>
internal sealed record DocumentBodyDto(
    Guid Id,
    string Bucket,
    string? DocType,
    string Origin,
    string ClarityResult,
    string ContentType,
    long SizeBytes,
    string PushStatus,
    bool BlobRetained,
    DateTime CreatedAt);

internal static class MediaFlows
{
    public const string DefaultFileName = "PLACEHOLDER-photo.jpg";

    /// <summary>
    /// A multipart body in the order the streamed endpoint requires: metadata parts, then the file.
    /// <paramref name="fileFirst"/> deliberately breaks that order for the test that pins it.
    /// </summary>
    public static MultipartFormDataContent Multipart(
        string? bucket,
        string? origin,
        byte[]? file,
        string contentType = ImageHeader.Jpeg,
        string fileName = DefaultFileName,
        bool fileFirst = false)
    {
        var content = new MultipartFormDataContent();

        void AddFile()
        {
            if (file is null)
            {
                return;
            }

            var part = new ByteArrayContent(file);
            part.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            content.Add(part, "file", fileName);
        }

        if (fileFirst)
        {
            AddFile();
        }

        if (bucket is not null)
        {
            content.Add(new StringContent(bucket), "bucket");
        }

        if (origin is not null)
        {
            content.Add(new StringContent(origin), "origin");
        }

        if (!fileFirst)
        {
            AddFile();
        }

        return content;
    }

    public static Task<HttpResponseMessage> Upload(
        HttpClient client, Guid assignmentId, MultipartFormDataContent body) =>
        client.PostAsync(new Uri($"/api/expert/assignments/{assignmentId}/documents", UriKind.Relative), body);

    /// <summary>The common case: a valid captured car photo into a capture-only bucket.</summary>
    public static Task<HttpResponseMessage> UploadCarPhoto(
        HttpClient client, Guid assignmentId, byte[]? image = null) =>
        Upload(
            client,
            assignmentId,
            Multipart(
                MediaBuckets.InsuredCarPhoto, DocumentOrigins.Captured, image ?? TestImages.Jpeg(1600, 1200)));

    public static async Task<Document> DocumentRow(this ApiFixture fixture, Guid documentId)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Documents.AsNoTracking().SingleAsync(d => d.Id == documentId);
    }

    public static async Task<Document?> FindDocumentRow(this ApiFixture fixture, Guid documentId)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Documents.AsNoTracking().SingleOrDefaultAsync(d => d.Id == documentId);
    }

    public static async Task<int> DocumentCountFor(this ApiFixture fixture, Guid assignmentId)
    {
        await using var db = fixture.CreateDbContext();
        return await db.Documents.CountAsync(d =>
            d.OwnerKind == DocumentOwnerKinds.Assignment && d.OwnerId == assignmentId);
    }

    /// <summary>The outbox row this document's upload queued (§7.3's join key, from the other side).</summary>
    public static async Task<Next3OutboxMessage> OutboxRowFor(this ApiFixture fixture, Guid documentId)
    {
        var document = await fixture.DocumentRow(documentId);
        Assert.NotNull(document.OutboxMessageId);
        return await fixture.Row(document.OutboxMessageId.Value);
    }

    /// <summary>
    /// Moves a confirmed push's <c>sent_at</c> back, so retention arithmetic can be tested at its real
    /// boundary without advancing the shared clock — which the whole serialized collection would then
    /// live with, and which past 15 minutes starts expiring access tokens (the 2.1 lesson).
    /// </summary>
    public static async Task BackdateSentAt(this ApiFixture fixture, Guid outboxMessageId, int days)
    {
        await using var db = fixture.CreateDbContext();
        var sentAt = fixture.Time.GetUtcNow().UtcDateTime.AddDays(-days);
        await db.Database.ExecuteSqlAsync(
            $"UPDATE next3_outbox SET sent_at = {sentAt} WHERE id = {outboxMessageId}");
    }

    public static Task<IReadOnlyDictionary<string, int>> Sweep(this ApiFixture fixture) =>
        fixture.Cleanup.RunOnce(CancellationToken.None);

    public static Task<bool> BlobExists(this ApiFixture fixture, string blobKey) =>
        fixture.Blobs.Exists(blobKey, CancellationToken.None);

    /// <summary>Runs a scenario with patched media options, then restores them (see <see cref="ApiFixture.Media"/>).</summary>
    public static async Task WithMediaOptions(
        this ApiFixture fixture, Action<MediaOptions> configure, Func<Task> scenario)
    {
        var original = fixture.Media.CurrentValue;
        var patched = new MediaOptions { MaxFileMb = original.MaxFileMb };
        Copy(original.ImageContentTypes, patched.ImageContentTypes);
        Copy(original.DocumentContentTypes, patched.DocumentContentTypes);
        configure(patched);

        fixture.Media.CurrentValue = patched;
        try
        {
            await scenario();
        }
        finally
        {
            fixture.Media.CurrentValue = original;
        }
    }

    public static async Task WithRetention(
        this ApiFixture fixture, Action<RetentionOptions> configure, Func<Task> scenario)
    {
        var original = fixture.Retention.CurrentValue;
        var patched = new RetentionOptions
        {
            BlobDays = original.BlobDays,
            BrokerBlobDays = original.BrokerBlobDays,
            OrphanBlobHours = original.OrphanBlobHours,
            OtpChallengeHours = original.OtpChallengeHours,
            CleanupEnabled = original.CleanupEnabled,
            PollMinutes = original.PollMinutes,
        };
        configure(patched);

        fixture.Retention.CurrentValue = patched;
        try
        {
            await scenario();
        }
        finally
        {
            fixture.Retention.CurrentValue = original;
        }
    }

    private static void Copy(IList<string> source, IList<string> destination)
    {
        foreach (var item in source)
        {
            destination.Add(item);
        }
    }
}
