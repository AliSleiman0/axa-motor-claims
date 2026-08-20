namespace Api.Integrations.Next3;

/// <summary>
/// The bound half of Appendix A's `Next3` section. Only the document-type map is here: the mode and
/// assignment-source switches are read once at startup in <c>ServiceRegistration</c>, where the
/// implementation is chosen, and binding them twice would invite the two copies to disagree.
/// </summary>
public sealed class Next3Options
{
    public const string SectionName = "Next3";

    /// <summary>
    /// NEXT3's document-type codes (#12), keyed by the names Appendix A gives them —
    /// `InsuredCarPhoto`, `ExpertReport` and so on. Every value is a PLACEHOLDER until #12 answers,
    /// and <see cref="Api.Modules.Media.MediaBuckets"/> maps a bucket to its key.
    /// </summary>
    public Dictionary<string, string> DocTypes { get; } = new(StringComparer.Ordinal);
}
