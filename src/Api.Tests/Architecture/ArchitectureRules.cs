using System.Reflection;
using Api.Integrations.Next3;
using Mono.Cecil;
using NetArchTest.Rules;

namespace Api.Tests.Architecture;

// The executable form of design.md §3's boundaries. Rules are parameterized by assembly so the
// self-tests can prove each rule detects a violation (an empty namespace passes any rule vacuously).
public static class ArchitectureRules
{
    public const string CompositionNamespace = "Api.Composition";
    public const string OutboxNamespace = "Api.Outbox";
    public const string PublicModuleNamespace = "Api.Modules.PublicSurface";
    public const string Next3Namespace = "Api.Integrations.Next3";
    public const string UsersModuleNamespace = "Api.Modules.Users";

    public static Assembly ApiAssembly => typeof(INext3Client).Assembly;

    // Rule 1: no feature module references RealNext3Client directly — only the DI registration may.
    public static TestResult OnlyDiRegistrationReferencesRealNext3Client(Assembly assembly, string allowedNamespace) =>
        Types.InAssembly(assembly)
            .That().DoNotResideInNamespace(allowedNamespace)
            .And().DoNotHaveName(nameof(RealNext3Client))
            .ShouldNot().HaveDependencyOn(typeof(RealNext3Client).FullName!)
            .GetResult();

    // Rule 2: nothing in the public module namespace references INext3Client or profile/user types.
    public static TestResult PublicModuleIsIsolated(Assembly assembly, string publicNamespace) =>
        Types.InAssembly(assembly)
            .That().ResideInNamespace(publicNamespace)
            .ShouldNot().HaveDependencyOnAny(Next3Namespace, UsersModuleNamespace)
            .GetResult();

    // Rule 3: only the outbox worker namespace calls push operations on INext3Client.
    // Method-level, so a custom Cecil rule: reads are legitimate feature-code calls (§5.1 search).
    public static TestResult OnlyOutboxCallsPushOperations(Assembly assembly, string allowedNamespace) =>
        Types.InAssembly(assembly)
            .That().DoNotResideInNamespace(allowedNamespace)
            .Should().MeetCustomRule(new DoesNotCallNext3PushOperationsRule())
            .GetResult();
}

public sealed class DoesNotCallNext3PushOperationsRule : ICustomRule
{
    private static readonly string[] PushOperations =
    [
        nameof(INext3Client.RecordArrival),
        nameof(INext3Client.UploadDocument),
    ];

    public bool MeetsRule(TypeDefinition type) => !CallsPushOperation(type);

    // Nested types included: async/lambda bodies compile into nested state-machine/closure types.
    private static bool CallsPushOperation(TypeDefinition type) =>
        type.Methods.Where(m => m.HasBody)
            .SelectMany(m => m.Body.Instructions)
            .Select(i => i.Operand)
            .OfType<MethodReference>()
            .Any(IsPushOperation)
        || type.NestedTypes.Any(CallsPushOperation);

    private static bool IsPushOperation(MethodReference method) =>
        method.DeclaringType.FullName == typeof(INext3Client).FullName
        && PushOperations.Contains(method.Name);
}
