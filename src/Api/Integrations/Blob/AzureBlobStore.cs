using System.Net;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Options;

namespace Api.Integrations.Blob;

/// <summary>
/// The real transit buffer (design.md §3): Azure Blob in deployment, Azurite on the developer's
/// machine — the same client and the same code path, which is the point of using the emulator rather
/// than a filesystem stand-in. Selected by <c>Blob:Mode = azure</c>; the connection string is
/// <c>ConnectionStrings:Blob</c> (<c>UseDevelopmentStorage=true</c> for Azurite).
///
/// Nothing here knows what a document is. Container layout, retention and the never-delete-before-
/// `sent` rule all live in the media module (§7.3); this type only moves bytes.
/// </summary>
public sealed class AzureBlobStore : IBlobStore
{
    private readonly Lazy<Task<BlobContainerClient>> _container;

    public AzureBlobStore(IConfiguration configuration, IOptions<BlobOptions> options)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);

        var connectionString = configuration.GetConnectionString("Blob")
            ?? throw new InvalidOperationException(
                "Connection string 'Blob' is missing, but Blob:Mode is 'azure'. "
                + "Use 'UseDevelopmentStorage=true' for Azurite (see CLAUDE.md).");

        var containerName = options.Value.ContainerName;

        // Lazily, once: creating the container on every Put would cost a round trip per upload, and
        // doing it at startup would make the API refuse to boot when storage is briefly unreachable.
        _container = new Lazy<Task<BlobContainerClient>>(async () =>
        {
            var client = new BlobServiceClient(connectionString).GetBlobContainerClient(containerName);

            // No public access: §9 allows blob reads through short-lived SAS only.
            await client.CreateIfNotExistsAsync(PublicAccessType.None);
            return client;
        });
    }

    public async Task Put(string key, Stream content, string contentType, CancellationToken ct)
    {
        var container = await _container.Value;
        await container.GetBlobClient(key).UploadAsync(
            content,
            new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = contentType } },
            ct);
    }

    public async Task<bool> Exists(string key, CancellationToken ct)
    {
        var container = await _container.Value;
        return await container.GetBlobClient(key).ExistsAsync(ct);
    }

    public async Task<Stream?> Open(string key, CancellationToken ct)
    {
        var container = await _container.Value;

        try
        {
            return await container.GetBlobClient(key).OpenReadAsync(cancellationToken: ct);
        }
        catch (RequestFailedException ex) when (ex.Status == (int)HttpStatusCode.NotFound)
        {
            // "Not there" is a state, not a fault (see IBlobStore.Open): §7.3 deletes a blob once its
            // push is confirmed, so the caller has to be able to tell an absent blob from a storage
            // outage — and an outage must stay an exception, because that one is worth retrying.
            return null;
        }
    }

    public async Task<bool> Delete(string key, CancellationToken ct)
    {
        var container = await _container.Value;
        return await container.GetBlobClient(key).DeleteIfExistsAsync(cancellationToken: ct);
    }

    public async Task<IReadOnlyList<BlobItem>> List(string prefix, CancellationToken ct)
    {
        var container = await _container.Value;
        var items = new List<BlobItem>();

        await foreach (var blob in container.GetBlobsAsync(
            BlobTraits.None, BlobStates.None, prefix, ct))
        {
            items.Add(new BlobItem(
                blob.Name,
                blob.Properties.ContentLength ?? 0,
                (blob.Properties.CreatedOn ?? default).UtcDateTime));
        }

        return items;
    }
}
