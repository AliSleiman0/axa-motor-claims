namespace Api.Integrations.Blob;

/// <summary>
/// Which blob implementation is live and where it writes (design.md Appendix A `Blob:*`).
/// Mirrors <c>Next3:Mode</c>: the fake is the default everywhere, and no test needs a storage
/// emulator running.
/// </summary>
public sealed class BlobOptions
{
    public const string SectionName = "Blob";

    /// <summary>`fake` (in-memory) or `azure` (Azurite locally, Azure Blob in deployment).</summary>
    public string Mode { get; set; } = "fake";

    /// <summary>
    /// The single transit container of §7.3. Not client data — it is our own storage layout — so it
    /// lives here as an ordinary config value rather than as a PLACEHOLDER.
    /// </summary>
    public string ContainerName { get; set; } = "media-transit";
}
