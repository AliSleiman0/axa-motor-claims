namespace Api.Integrations.Next3;

/// <summary>
/// The wire shapes of <c>docs/next3-openapi.yaml</c>, kept apart from §6.2's domain records so that
/// NEXT3 renaming a field changes this file and nothing else.
///
/// **Namespace-level rather than nested inside <c>RealNext3Client</c>, and that is a boundary rule
/// rather than a style choice.** Architecture rule 1 exempts only the type *named*
/// <c>RealNext3Client</c> from referencing <c>RealNext3Client</c>; a nested type is named
/// <c>Next3ClaimDto</c> and carries a dependency on its declaring type, so four nested records turned
/// rule 1 red the moment they existed. Moving them out satisfies the rule as written — no exemption
/// widened, no guard loosened. (Compiler-generated async state machines do *not* trip it: NetArchTest
/// filters those, which is worth knowing before anyone tries to "fix" the rule for them.)
///
/// <c>internal</c> because nothing outside this assembly has any business with NEXT3's wire format.
/// </summary>
internal sealed record Next3ClaimDto(
    string VisaNo,
    string PolicyNo,
    string PlateNo,
    string InsuredName,
    string InsuredPhone,
    string CarMakeModel,
    string City,
    DateOnly AccidentDate)
{
    public ClaimDetail ToClaimDetail() => new(
        VisaNo, PolicyNo, PlateNo, InsuredName, InsuredPhone, CarMakeModel, City, AccidentDate);
}

internal sealed record Next3ClaimSummaryDto(
    string VisaNo, string PlateNo, string InsuredName, string CarMakeModel, DateOnly AccidentDate)
{
    public ClaimSummary ToClaimSummary() => new(VisaNo, PlateNo, InsuredName, CarMakeModel, AccidentDate);
}

internal sealed record Next3ExpertDto(string Id, string Name, string Mobile, string Email, bool Active)
{
    public Next3Expert ToExpert() => new(Id, Name, Mobile, Email, Active);
}

internal sealed record Next3GarageDto(string Id, string Name, string Mobile, string Email, bool Active)
{
    public Next3Garage ToGarage() => new(Id, Name, Mobile, Email, Active);
}

/// <summary>
/// The arrival body. Carries the instant **and** the split, because §6.1 asks for "date, time" and #6
/// has not said which clock those are in — sending the zone alongside them means an answer of "UTC,
/// actually" is a config change rather than a re-push of rows that no longer hold the offset.
/// </summary>
internal sealed record ArrivalRequestDto(
    string ClientRef,
    DateTimeOffset OccurredAt,
    string Date,
    string Time,
    string TimeZone,
    double Latitude,
    double Longitude);
