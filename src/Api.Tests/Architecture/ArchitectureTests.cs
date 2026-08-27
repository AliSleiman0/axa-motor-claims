using NetArchTest.Rules;

namespace Api.Tests.Architecture;

public class ArchitectureTests
{
    [Fact]
    public void Rule1_OnlyComposition_MayReference_RealNext3Client()
    {
        var result = ArchitectureRules.OnlyDiRegistrationReferencesRealNext3Client(
            ArchitectureRules.ApiAssembly, ArchitectureRules.CompositionNamespace);

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Rule2_PublicModule_DoesNotReference_Next3_Or_UserTypes()
    {
        var result = ArchitectureRules.PublicModuleIsIsolated(
            ArchitectureRules.ApiAssembly, ArchitectureRules.PublicModuleNamespace);

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Rule2_IsNotVacuous_ThePublicModuleActuallyHasTypes()
    {
        // An empty namespace passes rule 2 trivially, which is how the rule sat green through
        // slices 1.1–1.4 while proving nothing. Slice 1.5 gave it real types; this asserts they
        // are still there, so deleting the module can never look like passing the boundary test.
        var types = ArchitectureRules.TypesInPublicModule(ArchitectureRules.ApiAssembly);

        Assert.NotEmpty(types);
    }

    [Fact]
    public void Rule3_OnlyOutbox_CallsPushOperations()
    {
        var result = ArchitectureRules.OnlyOutboxCallsPushOperations(
            ArchitectureRules.ApiAssembly, ArchitectureRules.OutboxNamespace);

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Rule4_OnlyOutbox_ReferencesOutboxRows()
    {
        var result = ArchitectureRules.OnlyOutboxReferencesOutboxRows(
            ArchitectureRules.ApiAssembly, ArchitectureRules.OutboxNamespace);

        Assert.True(result.IsSuccessful, Describe(result));
    }

    /// <summary>
    /// design.md §9.1's token is a bearer secret stored only as a hash; a log line is the one way it
    /// could reach durable storage in the clear anyway. The public module writes no logs at all today,
    /// and this is what stops the first "just while I debug this" from being permanent.
    /// </summary>
    [Fact]
    public void Rule5_ThePublicModuleNeverTouchesALogger()
    {
        var result = ArchitectureRules.PublicModuleNeverTouchesALogger(
            ArchitectureRules.ApiAssembly, ArchitectureRules.PublicModuleNamespace);

        Assert.True(result.IsSuccessful, Describe(result));
    }

    private static string Describe(TestResult result) =>
        result.IsSuccessful
            ? "OK"
            : "Boundary violations: " + string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>());
}
