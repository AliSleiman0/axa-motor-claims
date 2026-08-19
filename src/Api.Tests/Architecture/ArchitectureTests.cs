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
    public void Rule3_OnlyOutbox_CallsPushOperations()
    {
        var result = ArchitectureRules.OnlyOutboxCallsPushOperations(
            ArchitectureRules.ApiAssembly, ArchitectureRules.OutboxNamespace);

        Assert.True(result.IsSuccessful, Describe(result));
    }

    private static string Describe(TestResult result) =>
        result.IsSuccessful
            ? "OK"
            : "Boundary violations: " + string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>());
}
