using System.Collections.Concurrent;

namespace Api.Integrations.Next3;

/// <summary>
/// The fake NEXT3 (design.md §6.2). This is a deliverable, not a stub: it stays config-selectable all
/// the way to handover so UAT can run on it if the sandbox slips, and so support can reproduce a
/// push problem without touching AXA's core system.
///
/// Singleton — the seeded claims and the sent-log are its state, and they must survive across
/// requests and across outbox worker passes.
///
/// All data here is obviously fake by design (design.md Appendix A grep rule): no insurance types,
/// no real names, no plausible plate formats. If any of it starts looking like AXA data, that is a bug.
/// </summary>
public sealed class FakeNext3Client(FakeBehavior behavior) : INext3Client
{
    private readonly ConcurrentDictionary<string, ClaimDetail> _claims = SeedClaims();
    private readonly ConcurrentDictionary<string, Next3Expert> _experts = SeedExperts();
    private readonly ConcurrentDictionary<string, Next3Garage> _garages = SeedGarages();

    // The idempotency guard the outbox (slice 2.2) is written against: a clientRef is consumed only
    // by a *successful* push, so a retry after a timeout that actually succeeded is a silent no-op,
    // while a retry after a genuine failure still goes through.
    //
    // This is the fake modelling what #32 asks NEXT3 to guarantee. RealNext3Client keeps no
    // equivalent (slice 3.3): nothing on this side can know whether a timed-out push was accepted,
    // and architecture rule 4 forbids this namespace from reading outbox rows anyway. The guarantee
    // has to live at the receiver, which is why docs/next3-openapi.yaml states it as a requirement.
    private readonly ConcurrentDictionary<string, byte> _sentClientRefs = new(StringComparer.Ordinal);

    private readonly ConcurrentQueue<(string VisaNo, ArrivalInfo Info, string ClientRef)> _arrivals = new();
    private readonly ConcurrentQueue<(string VisaNo, DocumentPush Doc, string ClientRef)> _documents = new();

    /// <summary>Arrivals actually recorded — test observability, not part of the interface.</summary>
    internal IReadOnlyCollection<(string VisaNo, ArrivalInfo Info, string ClientRef)> RecordedArrivals =>
        [.. _arrivals];

    /// <summary>Documents actually recorded — test observability, not part of the interface.</summary>
    internal IReadOnlyCollection<(string VisaNo, DocumentPush Doc, string ClientRef)> RecordedDocuments =>
        [.. _documents];

    public async Task<ClaimDetail?> GetClaim(string visaNo, CancellationToken ct)
    {
        await behavior.Apply(nameof(GetClaim), ct);
        return _claims.GetValueOrDefault(visaNo);
    }

    public async Task<IReadOnlyList<ClaimSummary>> SearchClaims(string? plateNo, string? visaNo, CancellationToken ct)
    {
        await behavior.Apply(nameof(SearchClaims), ct);

        // Neither term given returns nothing rather than everything: the officer's visa search and
        // the expert's plate search are both deliberate lookups, never a "browse all claims" surface.
        if (string.IsNullOrWhiteSpace(plateNo) && string.IsNullOrWhiteSpace(visaNo))
        {
            return [];
        }

        return
        [
            .. _claims.Values
                .Where(c =>
                    (string.IsNullOrWhiteSpace(plateNo)
                        || c.PlateNo.Contains(plateNo, StringComparison.OrdinalIgnoreCase))
                    && (string.IsNullOrWhiteSpace(visaNo)
                        || c.VisaNo.Contains(visaNo, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(c => c.VisaNo, StringComparer.Ordinal)
                .Select(c => new ClaimSummary(c.VisaNo, c.PlateNo, c.InsuredName, c.CarMakeModel, c.AccidentDate)),
        ];
    }

    public async Task RecordArrival(string visaNo, ArrivalInfo info, string clientRef, CancellationToken ct)
    {
        await behavior.Delay(ct);

        if (AlreadySent(clientRef))
        {
            return;
        }

        behavior.MaybeFail(nameof(RecordArrival));
        RequireClaim(visaNo);

        _arrivals.Enqueue((visaNo, info, clientRef));
        MarkSent(clientRef);
    }

    public async Task UploadDocument(string visaNo, DocumentPush doc, string clientRef, CancellationToken ct)
    {
        await behavior.Delay(ct);

        if (AlreadySent(clientRef))
        {
            return;
        }

        behavior.MaybeFail(nameof(UploadDocument));
        RequireClaim(visaNo);

        _documents.Enqueue((visaNo, doc, clientRef));
        MarkSent(clientRef);
    }

    public async Task<IReadOnlyList<Next3Expert>> GetExperts(CancellationToken ct)
    {
        await behavior.Apply(nameof(GetExperts), ct);
        return [.. _experts.Values.OrderBy(e => e.Next3Id, StringComparer.Ordinal)];
    }

    public async Task<IReadOnlyList<Next3Garage>> GetGarages(CancellationToken ct)
    {
        await behavior.Apply(nameof(GetGarages), ct);
        return [.. _garages.Values.OrderBy(g => g.Next3Id, StringComparer.Ordinal)];
    }

    /// <summary>Adds or replaces a seeded claim — lets a test set up the claim its scenario needs.</summary>
    internal void Seed(ClaimDetail claim) => _claims[claim.VisaNo] = claim;

    /// <summary>Adds or replaces a seeded expert — slice 7.5's sync tests need specific rows.</summary>
    internal void Seed(Next3Expert expert) => _experts[expert.Next3Id] = expert;

    /// <summary>Adds or replaces a seeded garage — slice 7.5's sync tests need specific rows.</summary>
    internal void Seed(Next3Garage garage) => _garages[garage.Next3Id] = garage;

    /// <summary>
    /// Drops a seeded expert — simulates a supplier dropping out of NEXT3's network entirely
    /// (slice 7.5's sync tests), as opposed to <see cref="Seed(Next3Expert)"/> with `Active: false`,
    /// which simulates NEXT3 marking it inactive while still in network. The two are the same fact
    /// to the sync task (`MasterDataSyncTask`) but distinct NEXT3 answers worth testing separately.
    /// </summary>
    internal void RemoveExpert(string next3Id) => _experts.TryRemove(next3Id, out _);

    /// <summary>Drops a seeded garage — see <see cref="RemoveExpert"/>.</summary>
    internal void RemoveGarage(string next3Id) => _garages.TryRemove(next3Id, out _);

    private bool AlreadySent(string clientRef) => _sentClientRefs.ContainsKey(clientRef);

    private void MarkSent(string clientRef) => _sentClientRefs.TryAdd(clientRef, 0);

    // A push against a visa NEXT3 does not know is not transient — retrying will never fix it. It
    // throws something other than FakeTransientException so the outbox can tell the two apart and
    // drive this one to `failed` instead of retrying eight times (slice 2.2).
    private void RequireClaim(string visaNo)
    {
        if (!_claims.ContainsKey(visaNo))
        {
            throw new InvalidOperationException($"Unknown visa '{visaNo}'.");
        }
    }

    private static ConcurrentDictionary<string, ClaimDetail> SeedClaims()
    {
        ClaimDetail[] claims =
        [
            new("PLACEHOLDER-VISA-0001", "PLACEHOLDER-POL-0001", "PLC-TEST-01", "PLACEHOLDER Insured One",
                "+999000001001", "PLACEHOLDER Make One", "PLACEHOLDER City A", new DateOnly(2026, 8, 3)),
            new("PLACEHOLDER-VISA-0002", "PLACEHOLDER-POL-0002", "PLC-TEST-02", "PLACEHOLDER Insured Two",
                "+999000001002", "PLACEHOLDER Make Two", "PLACEHOLDER City A", new DateOnly(2026, 8, 7)),
            new("PLACEHOLDER-VISA-0003", "PLACEHOLDER-POL-0003", "PLC-TEST-03", "PLACEHOLDER Insured Three",
                "+999000001003", "PLACEHOLDER Make Three", "PLACEHOLDER City B", new DateOnly(2026, 8, 11)),
            new("PLACEHOLDER-VISA-0004", "PLACEHOLDER-POL-0004", "PLC-TEST-04", "PLACEHOLDER Insured Four",
                "+999000001004", "PLACEHOLDER Make Four", "PLACEHOLDER City B", new DateOnly(2026, 8, 14)),
            new("PLACEHOLDER-VISA-0005", "PLACEHOLDER-POL-0005", "PLC-TEST-05", "PLACEHOLDER Insured Five",
                "+999000001005", "PLACEHOLDER Make Five", "PLACEHOLDER City C", new DateOnly(2026, 8, 18)),
        ];

        return new ConcurrentDictionary<string, ClaimDetail>(
            claims.Select(c => KeyValuePair.Create(c.VisaNo, c)),
            StringComparer.Ordinal);
    }

    private static ConcurrentDictionary<string, Next3Expert> SeedExperts()
    {
        Next3Expert[] experts =
        [
            new("PLACEHOLDER-EXP-01", "PLACEHOLDER Expert One", "+999000002001",
                "expert-one@example.invalid", true),
            new("PLACEHOLDER-EXP-02", "PLACEHOLDER Expert Two", "+999000002002",
                "expert-two@example.invalid", true),
            // One inactive, so the §4 expert seed/sync job has a non-trivial case to handle (#8).
            new("PLACEHOLDER-EXP-03", "PLACEHOLDER Expert Three", "+999000002003",
                "expert-three@example.invalid", false),
        ];

        return new ConcurrentDictionary<string, Next3Expert>(
            experts.Select(e => KeyValuePair.Create(e.Next3Id, e)),
            StringComparer.Ordinal);
    }

    private static ConcurrentDictionary<string, Next3Garage> SeedGarages()
    {
        Next3Garage[] garages =
        [
            new("PLACEHOLDER-GAR-01", "PLACEHOLDER Garage One", "+999000003001",
                "garage-one@example.invalid", true),
            new("PLACEHOLDER-GAR-02", "PLACEHOLDER Garage Two", "+999000003002",
                "garage-two@example.invalid", true),
            // One inactive, same reason SeedExperts keeps one — the sync job (slice 7.5) needs a
            // non-trivial case to reconcile from the very first run.
            new("PLACEHOLDER-GAR-03", "PLACEHOLDER Garage Three", "+999000003003",
                "garage-three@example.invalid", false),
        ];

        return new ConcurrentDictionary<string, Next3Garage>(
            garages.Select(g => KeyValuePair.Create(g.Next3Id, g)),
            StringComparer.Ordinal);
    }
}
