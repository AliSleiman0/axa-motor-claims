using Api.Modules.Media;
using Microsoft.Extensions.Options;

namespace Api.Modules.Broker;

/// <summary>
/// The BRD's Option 1 upload kill-switch (design.md §7.1's Broker row, Appendix A
/// <c>Broker:AllowUpload</c>), applied over the static §7.1 table.
///
/// **The bucket rule is static and the switch is dynamic**, which is why this is an override rather
/// than a field on the row: §7.1 records what the BRD permits for broker documents (upload *or*
/// capture, with the provenance flag on every row), and the switch is a deployment-time decision AXA
/// can change without a release. Read through <see cref="IOptionsMonitor{TOptions}"/> per call so an
/// edit to the placeholder file — which is loaded with <c>reloadOnChange</c> — reaches the next
/// request, which is what makes B2's file control disappear live.
///
/// Applying it here rather than at the broker endpoint means the refusal comes out of the existing
/// <c>MediaValidation.CheckOrigin</c> path as <c>upload_not_allowed_for_bucket</c> — the same code a
/// capture-only bucket produces — and `GET /api/config/media` projects the same answer, from the same
/// place. Capture is unaffected in either state: the switch removes the picker, not the camera.
/// </summary>
public sealed class BrokerUploadSwitch(IOptionsMonitor<BrokerOptions> options) : IBucketRuleOverride
{
    public BucketRule Apply(BucketRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return string.Equals(rule.Bucket, MediaBuckets.BrokerDocument, StringComparison.Ordinal)
            ? rule with { AllowUpload = options.CurrentValue.AllowUpload }
            : rule;
    }
}
