using Api.Integrations.Next3;

namespace Api.Modules.Media;

/// <summary>What kind of file a bucket accepts, which decides its content-type allow-list.</summary>
public enum MediaKind
{
    /// <summary>Images only, and the §7.2 resolution floor applies.</summary>
    Image,

    /// <summary>Images or PDFs; there are no dimensions to check on a PDF.</summary>
    Document,

    /// <summary>
    /// Audio only (§7.2 item 4 — the voice note). No dimensions and no blur: the gate for these is a
    /// playback-confirm by a person, so the row records <c>not_applicable</c>. Acceptance is #10 and
    /// unanswered, which is why the allow-list is a placeholder key rather than a literal.
    /// </summary>
    Audio,
}

/// <summary>
/// One row of design.md §7.1's table. <see cref="AllowUpload"/> is the BRD's hard rule — car photos
/// are capture-only — and this record is the single place it is expressed, so 2.5's capture component
/// and 5.x's garage and broker flows read the same table rather than re-deciding it.
///
/// There is no <c>AllowCapture</c>: §7.1 marks capture "n-a" only for the expert report, which means
/// the question does not arise, not that a captured report must be refused. Refusing one would be an
/// invented restriction, and CLAUDE.md says take the smaller interpretation.
/// </summary>
public sealed record BucketRule(
    string Bucket,
    string OwnerKind,
    bool AllowUpload,
    string DocTypeKey,
    MediaKind Kind,
    string Next3Folder);

/// <summary>
/// design.md §7.1's bucket matrix, as data.
///
/// Only the expert rows are encoded. §7.1's garage row — "Documents (survey, discharge, invoice)" —
/// is one bucket carrying three different NEXT3 document-type codes, so it is not a 1:1 entry here,
/// and modelling it belongs to slice 5.1 with 5.1's knowledge of the declaration flow. The broker and
/// public rows (§5.3) push to NEXT3 not at all and get <c>push_status = n/a</c> instead of a doc type.
/// Each new bucket is a migration, because `bucket` is a check-constrained enum like every other §4
/// state column — which makes adding one a reviewable decision rather than a string appearing.
/// </summary>
public static class MediaBuckets
{
    public const string InsuredDocuments = "insured_documents";
    public const string InsuredCarPhoto = "insured_car_photo";
    public const string TpDocuments = "tp_documents";
    public const string TpCarPhoto = "tp_car_photo";
    public const string ExpertReport = "expert_report";
    public const string VoiceNote = "voice_note";
    public const string DamageDiagram = "damage_diagram";

    private static readonly Dictionary<string, BucketRule> Rules =
        new(StringComparer.Ordinal)
        {
            [InsuredDocuments] = new(
                InsuredDocuments, DocumentOwnerKinds.Assignment, AllowUpload: true,
                "InsuredDocument", MediaKind.Document, Next3Folders.ExpertDocuments),

            // Capture-only: the BRD's rule, and the reason the whole app exists is that these photos
            // must be the ones taken at the scene.
            [InsuredCarPhoto] = new(
                InsuredCarPhoto, DocumentOwnerKinds.Assignment, AllowUpload: false,
                "InsuredCarPhoto", MediaKind.Image, Next3Folders.ExpertDocuments),

            [TpDocuments] = new(
                TpDocuments, DocumentOwnerKinds.Assignment, AllowUpload: true,
                "TpDocument", MediaKind.Document, Next3Folders.ExpertDocuments),

            [TpCarPhoto] = new(
                TpCarPhoto, DocumentOwnerKinds.Assignment, AllowUpload: false,
                "TpCarPhoto", MediaKind.Image, Next3Folders.ExpertDocuments),

            // E5. §5.1: "upload allowed — a report is a document, not a car photo".
            [ExpertReport] = new(
                ExpertReport, DocumentOwnerKinds.Assignment, AllowUpload: true,
                "ExpertReport", MediaKind.Document, Next3Folders.ExpertDocuments),

            // §5.1's voice note and damage diagram (slice 3.1). Both land in *Expert documents* like
            // everything else the expert produces, and both are `AllowUpload: false` — not because
            // §7.1 marks them capture-only, but because they are produced inside the app and there is
            // no file to pick. An `origin: uploaded` on either is a client that has gone wrong.
            [VoiceNote] = new(
                VoiceNote, DocumentOwnerKinds.Assignment, AllowUpload: false,
                "VoiceNote", MediaKind.Audio, Next3Folders.ExpertDocuments),

            // Image, deliberately: the diagram is a canvas export, so §7.2's *server* resolution
            // floor applies to it exactly as it does to a photograph. That is what the web module's
            // fixed 1600x1200 render exists to clear.
            [DamageDiagram] = new(
                DamageDiagram, DocumentOwnerKinds.Assignment, AllowUpload: false,
                "DamageDiagram", MediaKind.Image, Next3Folders.ExpertDocuments),
        };

    /// <summary>Every bucket the schema currently allows — the source for the check constraint.</summary>
    public static IReadOnlyCollection<string> All => Rules.Keys;

    public static IReadOnlyCollection<BucketRule> AllRules => Rules.Values;

    public static BucketRule? Find(string? bucket) =>
        bucket is not null && Rules.TryGetValue(bucket, out var rule) ? rule : null;
}
