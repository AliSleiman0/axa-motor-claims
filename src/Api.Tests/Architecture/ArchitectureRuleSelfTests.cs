using System.Reflection;

namespace Api.Tests.Architecture;

// Each rule is run against this test assembly, which contains a deliberate violation per rule
// (Fixtures/). A rule that stops detecting its violation fails here — this is what keeps the
// boundary tests non-vacuous while the real module namespaces are still empty or missing.
public class ArchitectureRuleSelfTests
{
    private static Assembly FixtureAssembly => typeof(ArchitectureRuleSelfTests).Assembly;

    [Fact]
    public void Rule1_SelfTest_DetectsViolation()
    {
        var result = ArchitectureRules.OnlyDiRegistrationReferencesRealNext3Client(
            FixtureAssembly, ArchitectureRules.CompositionNamespace);

        Assert.False(result.IsSuccessful);
        Assert.Contains(typeof(Fixtures.Rule1.ViolatingRealClientConsumer).FullName!, result.FailingTypeNames);
    }

    [Fact]
    public void Rule2_SelfTest_DetectsViolation()
    {
        var result = ArchitectureRules.PublicModuleIsIsolated(
            FixtureAssembly, "Api.Tests.Architecture.Fixtures.PublicModule");

        Assert.False(result.IsSuccessful);
        Assert.Contains(typeof(Fixtures.PublicModule.ViolatingPublicType).FullName!, result.FailingTypeNames);
    }

    [Fact]
    public void Rule3_SelfTest_DetectsViolation()
    {
        var result = ArchitectureRules.OnlyOutboxCallsPushOperations(
            FixtureAssembly, ArchitectureRules.OutboxNamespace);

        Assert.False(result.IsSuccessful);
        Assert.Contains(typeof(Fixtures.Rule3.ViolatingPushCaller).FullName!, result.FailingTypeNames);
    }
}
