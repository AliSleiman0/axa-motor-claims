namespace Api.Modules.Expert;

/// <summary>What one delivery of an assignment did. Every value is a normal outcome, not an error.</summary>
public enum AssignmentIngestionResult
{
    /// <summary>A new assignment was recorded.</summary>
    Created,

    /// <summary>Already known by its NEXT3 ref — a replayed webhook or overlapping poll (§6.2).</summary>
    DuplicateIgnored,

    /// <summary>
    /// No expert_profile carries this NEXT3 id, so there is nobody to assign it to. Recorded in the
    /// audit log and dropped: retrying cannot fix data the app does not have (#8 — the expert
    /// seed/sync job does not exist yet).
    /// </summary>
    UnknownExpert,
}
