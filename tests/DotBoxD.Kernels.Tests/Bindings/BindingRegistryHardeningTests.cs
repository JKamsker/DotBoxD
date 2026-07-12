using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Bindings;

public sealed class BindingRegistryHardeningTests
{
    [Fact]
    public void Binding_registry_rejects_binding_without_effects()
    {
        var ex = Assert.Throws<SandboxValidationException>(() => Build(TestBinding(effects: SandboxEffect.None)));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-BINDING-EFFECT");
    }

    [Fact]
    public void Binding_registry_rejects_external_binding_without_audit()
    {
        var ex = Assert.Throws<SandboxValidationException>(() => Build(TestBinding(
            effects: SandboxEffect.HostStateRead,
            requiredCapability: "game.read",
            safety: BindingSafety.ReadOnlyExternal,
            grantValidator: NoParameters)));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-BINDING-AUDIT");
    }

    [Fact]
    public void Binding_registry_rejects_effectful_binding_without_audit_even_when_safety_is_mislabeled()
    {
        var ex = Assert.Throws<SandboxValidationException>(() => Build(TestBinding(
            effects: SandboxEffect.FileRead,
            requiredCapability: "file.read",
            safety: BindingSafety.PureHostFacade)));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-BINDING-AUDIT");
    }

    [Fact]
    public void Binding_registry_constructor_validates_descriptors()
    {
        var descriptors = new[]
        {
            TestBinding(
                effects: SandboxEffect.FileRead,
                requiredCapability: "file.read",
                safety: BindingSafety.PureHostFacade)
        };

        var ex = Assert.Throws<SandboxValidationException>(() => new BindingRegistry(descriptors));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-BINDING-AUDIT");
    }

    [Theory]
    [InlineData("constructor", "descriptor", "bindings")]
    [InlineData("add", "descriptor", null)]
    [InlineData("add-range", "descriptor", "descriptors")]
    public void Binding_registry_public_entrypoints_reject_null_descriptors(
        string scenario,
        string expectedParamName,
        string? alternateExpectedParamName)
    {
        var expectedParamNames = alternateExpectedParamName is null
            ? [expectedParamName]
            : new[] { expectedParamName, alternateExpectedParamName };
        var ex = Record.Exception(() => CreateRegistryWithNullDescriptor(scenario));

        Assert.NotNull(ex);
        Assert.False(
            ex is NullReferenceException,
            $"Expected public validation for a null binding descriptor, but got {ex.GetType().FullName}.");
        Assert.True(
            IsClosedNullDescriptorFailure(ex, expectedParamNames),
            $"Expected ArgumentException naming {string.Join("/", expectedParamNames)} or a binding validation diagnostic, but got {Describe(ex)}.");
    }

    [Theory]
    [InlineData("contains-null-id", "id")]
    [InlineData("try-get-null-id", "id")]
    [InlineData("get-descriptor-null-id", "id")]
    [InlineData("try-get-null-capability-id", "capabilityId")]
    public void Binding_registry_lookup_methods_reject_null_public_arguments(
        string scenario,
        string expectedParamName)
    {
        var registry = new BindingRegistry([]);

        var ex = Assert.Throws<ArgumentNullException>(() => InvokeNullLookup(registry, scenario));

        Assert.Equal(expectedParamName, ex.ParamName);
    }

    [Fact]
    public void Binding_registry_rejects_undefined_safety_classification()
    {
        var ex = Assert.Throws<SandboxValidationException>(() => Build(TestBinding(
            safety: (BindingSafety)999)));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-BINDING-SAFETY");
    }

    [Fact]
    public void Binding_registry_rejects_undefined_audit_level()
    {
        var ex = Assert.Throws<SandboxValidationException>(() => Build(TestBinding(
            auditLevel: (AuditLevel)999)));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-BINDING-AUDIT");
    }

    [Fact]
    public void Binding_registry_rejects_custom_capability_without_validator()
    {
        var ex = Assert.Throws<SandboxValidationException>(() => Build(TestBinding(
            effects: SandboxEffect.HostStateRead | SandboxEffect.Audit,
            requiredCapability: "game.read",
            safety: BindingSafety.ReadOnlyExternal,
            auditLevel: AuditLevel.PerCall)));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-BINDING-GRANT");
    }

    [Fact]
    public void Binding_registry_rejects_built_in_capability_with_unrelated_effects()
    {
        var ex = Assert.Throws<SandboxValidationException>(() => Build(TestBinding(
            effects: SandboxEffect.HostStateWrite | SandboxEffect.Audit,
            requiredCapability: "file.read",
            safety: BindingSafety.SideEffectingExternal,
            auditLevel: AuditLevel.PerCall)));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-BINDING-CAP-EFFECT");
    }

    [Fact]
    public void Binding_registry_composes_validators_for_shared_capability()
    {
        var registry = new BindingRegistryBuilder()
            .Add(TestBinding(
                id: "test.first",
                effects: SandboxEffect.HostStateRead | SandboxEffect.Audit,
                requiredCapability: "game.read",
                safety: BindingSafety.ReadOnlyExternal,
                auditLevel: AuditLevel.PerCall,
                grantValidator: (_, diagnostics) => diagnostics.Add(new SandboxDiagnostic("FIRST", "first"))))
            .Add(TestBinding(
                id: "test.second",
                effects: SandboxEffect.HostStateRead | SandboxEffect.Audit,
                requiredCapability: "game.read",
                safety: BindingSafety.ReadOnlyExternal,
                auditLevel: AuditLevel.PerCall,
                grantValidator: (_, diagnostics) => diagnostics.Add(new SandboxDiagnostic("SECOND", "second"))))
            .Build();
        var diagnostics = new List<SandboxDiagnostic>();

        Assert.True(registry.TryGetCapabilityGrantValidator("game.read", out var validator));
        validator(new CapabilityGrant("game.read", new Dictionary<string, string>()), diagnostics);

        Assert.Contains(diagnostics, d => d.Code == "FIRST");
        Assert.Contains(diagnostics, d => d.Code == "SECOND");
    }

    [Theory]
    [InlineData("F32")]
    [InlineData("Decimal")]
    [InlineData("Bytes")]
    [InlineData("Command")]
    public void Binding_registry_rejects_unsupported_scalar_types(string typeName)
    {
        var ex = Assert.Throws<SandboxValidationException>(() => Build(TestBinding(
            parameters: [SandboxType.Scalar(typeName)])));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-BINDING-TYPE");
    }

    [Theory]
    [InlineData("Object")]
    [InlineData("Dynamic")]
    [InlineData("Type")]
    [InlineData("IServiceProvider")]
    [InlineData("Stream")]
    [InlineData("HttpClient")]
    [InlineData("DbContext")]
    public void Binding_registry_rejects_forbidden_parameter_types(string typeName)
    {
        var ex = Assert.Throws<SandboxValidationException>(() => Build(TestBinding(
            parameters: [SandboxType.Scalar(typeName)])));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-BINDING-TYPE");
    }

    [Theory]
    [InlineData("Object")]
    [InlineData("Type")]
    [InlineData("Delegate")]
    [InlineData("Stream")]
    [InlineData("HttpClient")]
    [InlineData("RawDomainEntity")]
    public void Binding_registry_rejects_forbidden_return_types(string typeName)
    {
        var ex = Assert.Throws<SandboxValidationException>(() => Build(TestBinding(
            returnType: SandboxType.Scalar(typeName))));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-BINDING-TYPE");
    }

    [Fact]
    public void Binding_registry_rejects_non_hashable_map_key_type()
    {
        var type = SandboxType.Map(SandboxType.List(SandboxType.I32), SandboxType.I32);

        var ex = Assert.Throws<SandboxValidationException>(() => Build(TestBinding(parameters: [type])));

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-BINDING-TYPE");
    }

    private static BindingRegistry Build(BindingDescriptor binding)
        => new BindingRegistryBuilder().Add(binding).Build();

    private static void CreateRegistryWithNullDescriptor(string scenario)
    {
        switch (scenario)
        {
            case "constructor":
                _ = new BindingRegistry([null!]);
                return;
            case "add":
                _ = new BindingRegistryBuilder().Add(null!).Build();
                return;
            case "add-range":
                _ = new BindingRegistryBuilder().AddRange([null!]).Build();
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown null descriptor scenario.");
        }
    }

    private static void InvokeNullLookup(BindingRegistry registry, string scenario)
    {
        switch (scenario)
        {
            case "contains-null-id":
                _ = registry.Contains(null!);
                return;
            case "try-get-null-id":
                _ = registry.TryGet(null!, out _);
                return;
            case "get-descriptor-null-id":
                _ = registry.GetDescriptor(null!);
                return;
            case "try-get-null-capability-id":
                _ = registry.TryGetCapabilityGrantValidator(null!, out _);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown lookup scenario.");
        }
    }

    private static bool IsClosedNullDescriptorFailure(Exception ex, string[] expectedParamNames)
    {
        if (ex is ArgumentException argumentException)
        {
            return expectedParamNames.Contains(argumentException.ParamName, StringComparer.Ordinal);
        }

        if (ex is not SandboxValidationException validationException)
        {
            return false;
        }

        return validationException.Diagnostics.Any(
            d => d.Message.Contains("null", StringComparison.OrdinalIgnoreCase) &&
                d.Message.Contains("descriptor", StringComparison.OrdinalIgnoreCase));
    }

    private static string Describe(Exception ex)
    {
        if (ex is ArgumentException argumentException)
        {
            return $"{ex.GetType().FullName} ParamName={argumentException.ParamName}";
        }

        if (ex is SandboxValidationException validationException)
        {
            return string.Join(", ", validationException.Diagnostics.Select(d => $"{d.Code}: {d.Message}"));
        }

        return ex.GetType().FullName ?? ex.GetType().Name;
    }

    private static BindingDescriptor TestBinding(
        string id = "test.binding",
        SandboxEffect effects = SandboxEffect.Cpu,
        string? requiredCapability = null,
        BindingSafety safety = BindingSafety.PureHostFacade,
        AuditLevel auditLevel = AuditLevel.None,
        CapabilityGrantValidator? grantValidator = null,
        IReadOnlyList<SandboxType>? parameters = null,
        SandboxType? returnType = null)
        => new(
            id,
            SemVersion.One,
            parameters ?? [],
            returnType ?? SandboxType.Unit,
            effects,
            requiredCapability,
            BindingCostModel.Fixed(1),
            auditLevel,
            safety,
            (_, _, _) => ValueTask.FromResult(SandboxValue.Unit),
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(Kernels.Runtime.CompiledRuntime.CallBinding)),
            grantValidator);

    private static void NoParameters(CapabilityGrant grant, ICollection<SandboxDiagnostic> diagnostics)
    {
        foreach (var key in grant.Parameters.Keys)
        {
            diagnostics.Add(new SandboxDiagnostic(
                "E-POLICY-GRANT-PARAM",
                $"grant '{grant.Id}' parameter '{key}' is not supported"));
        }
    }
}
