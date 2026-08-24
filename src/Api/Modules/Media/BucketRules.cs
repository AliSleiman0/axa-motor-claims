namespace Api.Modules.Media;

/// <summary>
/// A module's chance to adjust the §7.1 row for a bucket it owns, from configuration the table itself
/// must not know about (slice 5.2).
///
/// There is exactly one implementation today — `BrokerUploadSwitch`, applying the BRD's
/// `Broker:AllowUpload` kill-switch to <c>broker_document</c>. It is an interface rather than a
/// reference from <see cref="MediaBuckets"/> to `BrokerOptions` so the dependency runs
/// **broker → media**, the direction every other module already runs; media is the shared kernel and
/// a reference back into a feature module would be a cycle at the module level.
///
/// Overrides must be **narrow and idempotent**: return the rule unchanged for every bucket that is not
/// yours. They are applied in registration order, and nothing today applies two to one bucket.
/// </summary>
public interface IBucketRuleOverride
{
    BucketRule Apply(BucketRule rule);
}

/// <summary>
/// §7.1's table as the application actually sees it: <see cref="MediaBuckets"/> plus whatever
/// configuration says today.
///
/// **Everything that asks "may this bucket take an uploaded file?" must ask here**, because there are
/// two askers and they must not be able to disagree: <c>MediaUploadService</c>, which refuses the
/// upload, and <c>GET /api/config/media</c>, which is what makes B2's file control *disappear* rather
/// than grey out. Computing the effective rule twice is the drift <see cref="MediaBuckets"/> exists to
/// prevent, read from the other end.
/// </summary>
public sealed class BucketRules(IEnumerable<IBucketRuleOverride> overrides)
{
    private readonly IBucketRuleOverride[] _overrides = [.. overrides];

    /// <summary>Every rule, with overrides applied — the projection `/api/config/media` serves.</summary>
    public IReadOnlyList<BucketRule> AllRules => [.. MediaBuckets.AllRules.Select(Effective)];

    /// <summary>Null for an unknown bucket, exactly as <see cref="MediaBuckets.Find"/> is.</summary>
    public BucketRule? Find(string? bucket) =>
        MediaBuckets.Find(bucket) is { } rule ? Effective(rule) : null;

    private BucketRule Effective(BucketRule rule)
    {
        foreach (var over in _overrides)
        {
            rule = over.Apply(rule);
        }

        return rule;
    }
}
