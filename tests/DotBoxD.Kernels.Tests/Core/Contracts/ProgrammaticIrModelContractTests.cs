using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Core.Contracts;

public sealed class ProgrammaticIrModelContractTests
{
    public static IEnumerable<object[]> NullConstructorCollections()
    {
        yield return [nameof(SandboxModule.CapabilityRequests), ThrowingAction(() => _ = new SandboxModule(
            "module",
            SemVersion.One,
            SandboxLanguage.CurrentVersion,
            null!,
            [],
            Metadata()))];
        yield return [nameof(SandboxModule.Metadata), ThrowingAction(() => _ = new SandboxModule(
            "module",
            SemVersion.One,
            SandboxLanguage.CurrentVersion,
            [],
            [],
            null!))];
        yield return [nameof(SandboxFunction.Parameters), ThrowingAction(() => _ = new SandboxFunction(
            "main",
            IsEntrypoint: true,
            null!,
            SandboxType.Unit,
            []))];
        yield return [nameof(CallExpression.Arguments), ThrowingAction(() => _ = new CallExpression(
            "helper",
            null!,
            GenericType: null,
            Span()))];
    }

    public static IEnumerable<object[]> NullInitCollections()
    {
        yield return [nameof(SandboxModule.CapabilityRequests), ThrowingAction(() => _ = ValidModule() with
        {
            CapabilityRequests = null!
        })];
        yield return [nameof(SandboxModule.Functions), ThrowingAction(() => _ = ValidModule() with
        {
            Functions = null!
        })];
        yield return [nameof(SandboxModule.Metadata), ThrowingAction(() => _ = ValidModule() with
        {
            Metadata = null!
        })];
        yield return [nameof(SandboxFunction.Parameters), ThrowingAction(() => _ = ValidFunction() with
        {
            Parameters = null!
        })];
        yield return [nameof(CallExpression.Arguments), ThrowingAction(() => _ = ValidCall() with
        {
            Arguments = null!
        })];
    }

    [Theory]
    [MemberData(nameof(NullConstructorCollections))]
    public void Constructors_reject_null_ir_collections_with_public_parameter_name(
        string expectedParamName,
        Action action)
    {
        var exception = Assert.Throws<ArgumentNullException>(action);

        Assert.Equal(expectedParamName, exception.ParamName);
    }

    [Theory]
    [MemberData(nameof(NullInitCollections))]
    public void Init_setters_reject_null_ir_collections_with_public_property_name(
        string expectedParamName,
        Action action)
    {
        var exception = Assert.Throws<ArgumentNullException>(action);

        Assert.Equal(expectedParamName, exception.ParamName);
    }

    private static SandboxModule ValidModule()
        => new(
            "module",
            SemVersion.One,
            SandboxLanguage.CurrentVersion,
            [],
            [ValidFunction()],
            Metadata());

    private static SandboxFunction ValidFunction()
        => new(
            "main",
            IsEntrypoint: true,
            [],
            SandboxType.Unit,
            [new ReturnStatement(new LiteralExpression(SandboxValue.Unit, Span()), Span())]);

    private static CallExpression ValidCall()
        => new("helper", [], GenericType: null, Span());

    private static SourceSpan Span()
        => new(0, 0);

    private static IReadOnlyDictionary<string, string> Metadata()
        => new Dictionary<string, string>(StringComparer.Ordinal);

    private static Action ThrowingAction(Action action)
        => action;
}
