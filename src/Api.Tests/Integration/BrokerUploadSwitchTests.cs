using System.Net;
using System.Net.Http.Json;
using Api.Modules.Media;

namespace Api.Tests.Integration;

/// <summary>
/// The BRD's Option 1 upload kill-switch (design.md §7.1, Appendix A `Broker:AllowUpload`).
///
/// The rule it enforces is one config value with **two consumers that must not disagree**: the upload,
/// which refuses a picked file, and `GET /api/config/media`, which is what makes B2's file control
/// *disappear* rather than grey out — "a disabled picker is a picker somebody finds a way to use".
/// Both go through `BucketRules`, and these tests are what would go red if either ever stopped.
/// </summary>
[Collection("api")]
public sealed class BrokerUploadSwitchTests(ApiFixture fixture)
{
    [Fact]
    public async Task WithTheSwitchOff_TheConfigEndpointRetractsTheBrokerBucketAndNothingElse()
    {
        var before = await BucketConfig();

        await fixture.WithUploadSwitch(allowUpload: false, async () =>
        {
            var after = await BucketConfig();

            Assert.False(after[MediaBuckets.BrokerDocument]);

            // Only that bucket. The switch is the broker's, and a change that reached §7.1's other
            // rows would silently turn off a garage's discharge upload as well.
            foreach (var (bucket, allowed) in before.Where(b => b.Key != MediaBuckets.BrokerDocument))
            {
                Assert.Equal(allowed, after[bucket]);
            }

            await Task.CompletedTask;
        });

        // And it comes back without a restart, which is what `reloadOnChange` buys.
        Assert.True((await BucketConfig())[MediaBuckets.BrokerDocument]);
    }

    [Fact]
    public async Task WithTheSwitchOff_APickedFileIsRefusedAndACapturedOneIsNot()
    {
        using var broker = await fixture.CreateBroker();
        var id = await broker.CreateRequestDraft();

        await fixture.WithUploadSwitch(allowUpload: false, async () =>
        {
            var picked = await broker.UploadBrokerDocument(id);

            Assert.Equal(HttpStatusCode.BadRequest, picked.StatusCode);
            Assert.Equal("upload_not_allowed_for_bucket", await ErrorCode(picked));
            Assert.Empty(await fixture.BrokerDocumentRows(id));

            // The switch removes the picker, not the camera: §7.1's Broker row keeps capture in both
            // states, and the whole point of throwing it is that AXA can insist on photographs taken
            // in the app.
            (await broker.CaptureBrokerDocument(id)).EnsureSuccessStatusCode();
            Assert.Single(await fixture.BrokerDocumentRows(id));
        });
    }

    [Fact]
    public async Task WithTheSwitchOn_APickedFileIsAccepted()
    {
        using var broker = await fixture.CreateBroker();
        var id = await broker.CreateRequestDraft();

        // The non-vacuity half: without it the test above passes for a bucket that never allowed
        // uploads at all, and the switch could be doing nothing.
        (await broker.UploadBrokerDocument(id)).EnsureSuccessStatusCode();

        var document = Assert.Single(await fixture.BrokerDocumentRows(id));
        Assert.Equal(DocumentOrigins.Uploaded, document.Origin);
    }

    private async Task<Dictionary<string, bool>> BucketConfig()
    {
        using var client = fixture.CreateClient();
        var config = await client.GetFromJsonAsync<MediaConfigBody>("/api/config/media");
        return config!.Buckets.ToDictionary(b => b.Bucket, b => b.AllowUpload, StringComparer.Ordinal);
    }

    private static async Task<string?> ErrorCode(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ErrorBody>())?.Error;

    private sealed record MediaConfigBody(IReadOnlyList<BucketConfigBody> Buckets);

    private sealed record BucketConfigBody(string Bucket, bool AllowUpload);

    private sealed record ErrorBody(string Error);
}
