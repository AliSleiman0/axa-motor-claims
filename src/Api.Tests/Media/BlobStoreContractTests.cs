using System.Text;
using Api.Integrations.Blob;
using Api.Modules.Media;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Api.Tests.Media;

/// <summary>
/// One contract, two implementations — the shape design.md §6.2 asks of every port ("the fake is a
/// deliverable, not a stub") and the shape slice 3.3 will need for fake-vs-sandbox NEXT3.
///
/// The fake runs always: it is what the rest of the suite depends on, and §7.3's lifecycle tests are
/// only as trustworthy as it is. The Azure adapter runs against Azurite when the emulator is up and
/// skips when it is not, so `dotnet test` never needs Docker but the real client is not untested code
/// either.
/// </summary>
public class BlobStoreContractTests
{
    private const string Prefix = "contract-test";

    [Fact]
    public Task TheInMemoryStore_SatisfiesTheContract() =>
        AssertContract(new InMemoryBlobStore(TimeProvider.System));

    [AzuriteFact]
    public Task TheAzureStore_SatisfiesTheContract()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Blob"] = "UseDevelopmentStorage=true",
            })
            .Build();

        var options = Options.Create(new BlobOptions { ContainerName = "media-transit-test" });

        return AssertContract(new AzureBlobStore(configuration, options));
    }

    private static async Task AssertContract(IBlobStore store)
    {
        // Namespaced per run: the emulator's container persists between runs, and a contract test
        // that assumes an empty container is a contract test that fails the second time.
        var run = $"{Prefix}/{Guid.CreateVersion7():N}";
        var key = $"{run}/PLACEHOLDER-photo.jpg";
        var ct = CancellationToken.None;

        try
        {
            Assert.False(await store.Exists(key, ct));

            await store.Put(key, Stream("PLACEHOLDER-contents"), "image/jpeg", ct);
            Assert.True(await store.Exists(key, ct));

            var listed = await store.List(run, ct);
            var item = Assert.Single(listed);
            Assert.Equal(key, item.Key);
            Assert.Equal(20, item.SizeBytes);

            // The orphan sweep's grace window is arithmetic on this value, so it has to be real.
            Assert.NotEqual(default, item.CreatedAt);

            // Overwrite: an upload retried against the same key must not append or fail.
            await store.Put(key, Stream("PLACEHOLDER-replacement-contents"), "image/jpeg", ct);
            var replaced = Assert.Single(await store.List(run, ct));
            Assert.Equal(32, replaced.SizeBytes);

            // A prefix that matches nothing returns nothing rather than everything — the orphan sweep
            // would otherwise delete the container.
            Assert.Empty(await store.List($"{run}-no-such-prefix", ct));

            // **Non-seekable, unknown-length.** This is what the upload path actually hands a store:
            // Kestrel's request body wrapped in PrefixedStream + LimitedStream, none of which can
            // seek or report a length. A store that quietly required a seekable stream would pass
            // every test above and fail on the first real photograph — and the composition here is
            // the same one MediaUploadService builds, not an approximation of it.
            var streamedKey = $"{run}/PLACEHOLDER-streamed.jpg";
            using (var source = Stream("PLACEHOLDER-streamed-contents"))
            {
                var streamed = new LimitedStream(
                    new PrefixedStream(ReadOnlyMemory<byte>.Empty, source), long.MaxValue);

                Assert.False(streamed.CanSeek);
                await store.Put(streamedKey, streamed, "image/jpeg", ct);
            }

            Assert.True(await store.Exists(streamedKey, ct));
            Assert.Equal(
                29, (await store.List(run, ct)).Single(b => b.Key == streamedKey).SizeBytes);
            await store.Delete(streamedKey, ct);

            // Deletion is idempotent: §7.3's sweep re-issues a delete whenever a previous pass failed
            // after removing the bytes, and that second call must be a quiet false, not a throw.
            Assert.True(await store.Delete(key, ct));
            Assert.False(await store.Exists(key, ct));
            Assert.False(await store.Delete(key, ct));
        }
        finally
        {
            await store.Delete(key, ct);
        }
    }

    private static MemoryStream Stream(string content) => new(Encoding.UTF8.GetBytes(content));
}
