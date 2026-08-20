using Api.Integrations.Blob;
using Api.Integrations.Next3;
using Api.Outbox;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Api.Tests.Integrations;

/// <summary>
/// One contract, two implementations — the shape design.md §6.2 asks of every port ("the fake is a
/// deliverable, not a stub"), applied to NEXT3 exactly as <c>BlobStoreContractTests</c> applies it to
/// blob storage.
///
/// The fake runs always: it is what every environment runs on today, what the week-4 demo runs on, and
/// what UAT will run on if the sandbox slips (§6.2 says so out loud). The real client runs against the
/// sandbox the day one exists, and skips with a message until then (#1).
///
/// **What "the contract" is here is worth being precise about.** It is not the exception *type* — the
/// fake throws <c>InvalidOperationException</c> for an unknown visa and the real client throws
/// <c>Next3RejectedException</c>, and both are correct. It is the **disposition**: whether the outbox
/// will retry the failure for 26 h 36 m or put it on A2 now. So that is what is asserted, through the
/// real classifier.
/// </summary>
public class Next3ClientContractTests
{
    [Fact]
    public Task TheFakeClient_SatisfiesTheContract()
    {
        var client = new FakeNext3Client(FakeTestHarness.Build().Behavior);

        // The fake seeds this one (§6.2). The sandbox half supplies its own, because there is no way
        // to guess a visa that exists in AXA's environment.
        return AssertContract(client, "PLACEHOLDER-VISA-0001", fakeExtras: client);
    }

    [SandboxFact]
    public Task TheRealClient_SatisfiesTheContract()
    {
        var options = new Next3Options
        {
            BaseUrl = SandboxFactAttribute.SandboxUrl,
            AuthMode = Next3AuthModes.ApiKey,
            ApiKey = SandboxFactAttribute.SandboxApiKey,
            ArrivalTimeZone = "Asia/Dubai",
            TimeoutSeconds = 30,
        };

        var time = new FakeTimeProvider(new DateTimeOffset(2026, 8, 21, 12, 0, 0, TimeSpan.Zero));
        var factory = new SandboxHttpClientFactory(new Uri(options.BaseUrl.TrimEnd('/') + "/"));
        var blobs = new InMemoryBlobStore(time);
        var tokens = new Next3TokenProvider(factory, Options.Create(options), time);
        var client = new RealNext3Client(factory, Options.Create(options), blobs, tokens);

        return AssertContract(
            client, SandboxFactAttribute.SandboxVisa, fakeExtras: null, blobs: blobs);
    }

    /// <param name="fakeExtras">
    /// The fake, when it is the implementation under test — used for the one assertion only it can
    /// make. See the comment at its use: a sandbox exposes no way to count what it stored, so
    /// "the replay did not create a duplicate" is **proven on the fake and requested of NEXT3 in
    /// docs/next3-openapi.yaml (#32)**, rather than pretended here.
    /// </param>
    private static async Task AssertContract(
        INext3Client client,
        string knownVisa,
        FakeNext3Client? fakeExtras,
        InMemoryBlobStore? blobs = null)
    {
        var ct = CancellationToken.None;

        // Namespaced per run, as BlobStoreContractTests namespaces its blob prefix: a clientRef is
        // consumed for ever by a successful push, so a fixed one would pass once and then be
        // deduplicated away on every later run against the same sandbox.
        var run = Guid.CreateVersion7().ToString("N");
        var unknownVisa = $"PLACEHOLDER-VISA-NO-SUCH-{run}";

        // 1. A visa NEXT3 does not know reads as null, never as an exception. §4's cache renders a
        //    different screen for "no such claim" than for "NEXT3 is down"; one return value carries
        //    that distinction, and losing it would blame the data for an outage.
        Assert.Null(await client.GetClaim(unknownVisa, ct));

        // 2. Neither search term returns nothing rather than everything. The officer's lookup is a
        //    deliberate question; "every claim you hold" is not one this application may ask.
        Assert.Empty(await client.SearchClaims(null, null, ct));
        Assert.Empty(await client.SearchClaims("   ", "   ", ct));

        // 3. A write against an unknown visa fails in a way the outbox will NOT retry. The two
        //    implementations throw different types on purpose and both are right — what has to agree
        //    is that neither burns 26 h 36 m of the retry schedule on a claim that does not exist.
        var rejected = await Record.ExceptionAsync(() =>
            client.RecordArrival(unknownVisa, Arrival(), $"REF-{run}-unknown", ct));

        Assert.NotNull(rejected);
        Assert.False(OutboxProcessor.IsTransient(rejected));

        // 4. An arrival against a real claim is accepted...
        var arrivalRef = $"REF-{run}-arrival";
        await client.RecordArrival(knownVisa, Arrival(), arrivalRef, ct);

        // ...and replaying it is accepted too, rather than throwing. This is the property the whole
        // outbox leans on: a push that timed out may well have succeeded, so the worker resends it,
        // and a second copy must be absorbed silently instead of failing the row or duplicating it.
        await client.RecordArrival(knownVisa, Arrival(), arrivalRef, ct);

        // 5. And a document round-trips, replay included.
        var documentRef = $"REF-{run}-document";
        var blobKey = $"contract/{run}/PLACEHOLDER-photo.jpg";

        if (blobs is not null)
        {
            await blobs.Put(blobKey, Bytes(), "image/jpeg", ct);
        }

        var document = new DocumentPush(
            Next3Folders.ExpertDocuments, "PLACEHOLDER-DOC-01", "PLACEHOLDER-photo.jpg", "image/jpeg",
            blobKey);

        await client.UploadDocument(knownVisa, document, documentRef, ct);
        await client.UploadDocument(knownVisa, document, documentRef, ct);

        // 6. Master data comes back as a list, never null — the seed job iterates it without a guard.
        Assert.NotNull(await client.GetExperts(ct));

        // 7. **Only the fake can prove the replay stored nothing the second time.** NEXT3 exposes no
        //    read-back of what a claim holds, so against the sandbox this assertion is impossible and
        //    is deliberately not faked: #32 asks AXA to guarantee it, and next3-openapi.yaml states it
        //    as a requirement. Recorded rather than quietly skipped — if this ever reads green against
        //    a sandbox, someone has made it lie.
        if (fakeExtras is not null)
        {
            Assert.Single(fakeExtras.RecordedArrivals);
            Assert.Single(fakeExtras.RecordedDocuments);
        }
    }

    private static ArrivalInfo Arrival() =>
        new(new DateTimeOffset(2026, 8, 21, 9, 30, 0, TimeSpan.Zero), 25.2048, 55.2708);

    private static MemoryStream Bytes() =>
        new(System.Text.Encoding.UTF8.GetBytes("PLACEHOLDER-contract-test-bytes"));

    /// <summary>A real transport, used only when a sandbox is configured.</summary>
    private sealed class SandboxHttpClientFactory(Uri baseAddress) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new() { BaseAddress = baseAddress };
    }
}
