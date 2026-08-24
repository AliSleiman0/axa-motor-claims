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
/// When a bucket's media joins the NEXT3 queue (design.md §5.2, slice 4.1).
///
/// Until this existed every upload wrote its outbox row in the same transaction as the document row,
/// which is right for the expert — the photo is wanted at AXA immediately — and wrong for the garage,
/// because §5.2 says "nothing goes to NEXT3 before approval" and had no mechanism to say it with.
/// </summary>
public enum PushTiming
{
    /// <summary>Queued at upload: the §5.1 expert path, and today's behaviour.</summary>
    Immediate,

    /// <summary>
    /// Stored <c>deferred</c> with no outbox row, and queued by §5.2's approve transition once the
    /// officer has linked a visa. There is no visa to push under before that, which is the other half
    /// of why the wait is structural rather than a policy someone applies.
    /// </summary>
    OnApproval,

    /// <summary>
    /// Never pushed — <c>push_status = n/a</c>. §5.3's broker and public-customer media, whose
    /// terminal act is an email; reserved here for slice 5.2, so no bucket carries it yet.
    /// </summary>
    Never,
}

/// <summary>
/// Which NEXT3 operation a bucket's push is queued as (design.md §4's `next3_outbox.operation`).
///
/// The wire call is the same either way — §6.2's interface has no `push_approval` method — but A2 has
/// to be able to tell "the approval never reached NEXT3" from "a photo never reached NEXT3", and that
/// distinction is a property of the bucket. Keeping it here rather than in the approve handler is the
/// point: a service that tested <c>bucket == "approval_image"</c> would be a §7.1 rule living outside
/// the table §7.1 is encoded in, which is exactly the drift this class exists to prevent.
/// </summary>
public enum Next3PushKind
{
    Document,
    Approval,
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
    string Next3Folder,
    PushTiming Timing,
    Next3PushKind PushKind = Next3PushKind.Document);

/// <summary>
/// design.md §7.1's bucket matrix, as data.
///
/// The expert rows (§5.1) and the declaration rows (§5.2) are encoded. **The declaration buckets moved
/// here in slice 4.1**, superseding this comment's earlier note that they belonged to 5.1: G2 attaches
/// documents at Draft, so the state machine cannot ship without them. §7.1's garage documents row —
/// "Documents (survey, discharge, invoice)" — is one bucket carrying several NEXT3 document-type
/// codes; 4.1 encoded the *survey* code it needed, and **slice 5.1 split the rest into the three G4
/// repair buckets below**, which is why `garage_documents` now means the survey paperwork attached
/// before submission and nothing else.
///
/// The broker and public rows (§5.3) push to NEXT3 not at all and will carry
/// <see cref="PushTiming.Never"/> with <c>push_status = n/a</c> instead of a doc type.
///
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
    public const string GarageDocuments = "garage_documents";
    public const string GarageCarPhoto = "garage_car_photo";
    public const string ApprovalImage = "approval_image";
    public const string RepairPhoto = "repair_photo";
    public const string Discharge = "discharge";
    public const string Invoice = "invoice";

    private static readonly Dictionary<string, BucketRule> Rules =
        new(StringComparer.Ordinal)
        {
            [InsuredDocuments] = new(
                InsuredDocuments, DocumentOwnerKinds.Assignment, AllowUpload: true,
                "InsuredDocument", MediaKind.Document, Next3Folders.ExpertDocuments, PushTiming.Immediate),

            // Capture-only: the BRD's rule, and the reason the whole app exists is that these photos
            // must be the ones taken at the scene.
            [InsuredCarPhoto] = new(
                InsuredCarPhoto, DocumentOwnerKinds.Assignment, AllowUpload: false,
                "InsuredCarPhoto", MediaKind.Image, Next3Folders.ExpertDocuments, PushTiming.Immediate),

            [TpDocuments] = new(
                TpDocuments, DocumentOwnerKinds.Assignment, AllowUpload: true,
                "TpDocument", MediaKind.Document, Next3Folders.ExpertDocuments, PushTiming.Immediate),

            [TpCarPhoto] = new(
                TpCarPhoto, DocumentOwnerKinds.Assignment, AllowUpload: false,
                "TpCarPhoto", MediaKind.Image, Next3Folders.ExpertDocuments, PushTiming.Immediate),

            // E5. §5.1: "upload allowed — a report is a document, not a car photo".
            [ExpertReport] = new(
                ExpertReport, DocumentOwnerKinds.Assignment, AllowUpload: true,
                "ExpertReport", MediaKind.Document, Next3Folders.ExpertDocuments, PushTiming.Immediate),

            // §5.1's voice note and damage diagram (slice 3.1). Both land in *Expert documents* like
            // everything else the expert produces, and both are `AllowUpload: false` — not because
            // §7.1 marks them capture-only, but because they are produced inside the app and there is
            // no file to pick. An `origin: uploaded` on either is a client that has gone wrong.
            [VoiceNote] = new(
                VoiceNote, DocumentOwnerKinds.Assignment, AllowUpload: false,
                "VoiceNote", MediaKind.Audio, Next3Folders.ExpertDocuments, PushTiming.Immediate),

            // Image, deliberately: the diagram is a canvas export, so §7.2's *server* resolution
            // floor applies to it exactly as it does to a photograph. That is what the web module's
            // fixed 1600x1200 render exists to clear.
            [DamageDiagram] = new(
                DamageDiagram, DocumentOwnerKinds.Assignment, AllowUpload: false,
                "DamageDiagram", MediaKind.Image, Next3Folders.ExpertDocuments, PushTiming.Immediate),

            // §5.2's three declaration buckets (slice 4.1). All land in the *Survey* folder — the name
            // the BRD gives the folder a garage-initiated claim ends up in — and all wait for the
            // officer's approval, because at Draft there is no visa to push them under and §5.2 pushes
            // nothing on rejection.

            // G2's supporting documents. Upload allowed: a garage scans a survey form or an invoice,
            // and §7.1's garage documents row marks both provenances acceptable.
            [GarageDocuments] = new(
                GarageDocuments, DocumentOwnerKinds.Declaration, AllowUpload: true,
                "SurveyDocument", MediaKind.Document, Next3Folders.Survey, PushTiming.OnApproval),

            // Capture-only, the same BRD rule as the expert's car photos and for the same reason: the
            // damage has to be the damage on the car that is actually in the garage.
            [GarageCarPhoto] = new(
                GarageCarPhoto, DocumentOwnerKinds.Declaration, AllowUpload: false,
                "GarageCarPhoto", MediaKind.Image, Next3Folders.Survey, PushTiming.OnApproval),

            // §5.2's "approval and comments captured as an image" (#18). Produced in-app by the
            // officer's browser (slice 4.2 renders it), so there is no file to pick — `AllowUpload:
            // false` for the 3.1 reason rather than the capture-only one. Image, so §7.2's server
            // resolution floor applies to the render exactly as to a photograph.
            [ApprovalImage] = new(
                ApprovalImage, DocumentOwnerKinds.Declaration, AllowUpload: false,
                "ApprovalImage", MediaKind.Image, Next3Folders.Survey, PushTiming.OnApproval,
                Next3PushKind.Approval),

            // §5.2's last row — G4's post-repair uploads (slice 5.1). Same owner kind and same
            // *Survey* folder as the three above, and **`Immediate` rather than `OnApproval`**: these
            // are attached at `repairs_in_progress`, which `CK_declaration_decision` guarantees has a
            // visa, so there is nothing left to wait for. Deferring them would be worse than
            // pointless — the only transition that drains the deferred set is `approve`, which has
            // already happened, so they would sit `deferred` for ever.

            // Capture-only, the same BRD rule as every other car photo: the point of a post-repair
            // photograph is that it is of the car that was actually repaired.
            [RepairPhoto] = new(
                RepairPhoto, DocumentOwnerKinds.Declaration, AllowUpload: false,
                "RepairPhoto", MediaKind.Image, Next3Folders.Survey, PushTiming.Immediate),

            // Discharge and invoice are paperwork, so upload is allowed and so is a photograph of a
            // paper original — §7.1's garage documents row marks both provenances acceptable, and
            // `origin` records which one every row was.
            [Discharge] = new(
                Discharge, DocumentOwnerKinds.Declaration, AllowUpload: true,
                "Discharge", MediaKind.Document, Next3Folders.Survey, PushTiming.Immediate),

            [Invoice] = new(
                Invoice, DocumentOwnerKinds.Declaration, AllowUpload: true,
                "Invoice", MediaKind.Document, Next3Folders.Survey, PushTiming.Immediate),
        };

    /// <summary>
    /// The buckets a garage fills **before** submission (§5.2's G2), as distinct from the three it
    /// fills after the repair. Named here rather than listed at each call site for
    /// <see cref="DocumentPushStatuses.WithoutOutboxRow"/>'s reason: G3's upload gate and the officer's
    /// allow-list both ask which side of the decision a bucket falls on, and two hand-written lists
    /// would eventually disagree about a fourth one.
    /// </summary>
    public static readonly string[] GarageDeclaration = [GarageDocuments, GarageCarPhoto];

    /// <summary>
    /// §5.2's G4 buckets — the ones a garage may only fill at `repairs_in_progress`, and the set
    /// `submit-repair-docs` requires at least one document from.
    /// </summary>
    public static readonly string[] Repair = [RepairPhoto, Discharge, Invoice];

    /// <summary>Every bucket the schema currently allows — the source for the check constraint.</summary>
    public static IReadOnlyCollection<string> All => Rules.Keys;

    public static IReadOnlyCollection<BucketRule> AllRules => Rules.Values;

    public static BucketRule? Find(string? bucket) =>
        bucket is not null && Rules.TryGetValue(bucket, out var rule) ? rule : null;
}
