using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Validation;

namespace DotBoxD.Kernels.Tests.Validation;

public sealed class ModuleValidatorCustomCatalogCapabilityValidationTests
{
    private static readonly SourceSpan Span = new(0, 0);

    [Fact]
    public void ModuleValidator_rejects_custom_catalog_side_effects_without_capability()
    {
        var result = new ModuleValidator().Validate(
            ModuleCallingCustomWrite(),
            new CustomCatalog(CustomWriteSignature()),
            HostStateWritePolicy());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "E-BINDING-CAP");
    }

    [Fact]
    public void BindingRegistry_rejects_equivalent_side_effecting_descriptor_without_capability()
    {
        var ex = Assert.Throws<SandboxValidationException>(() =>
            new BindingRegistryBuilder()
                .Add(CustomWriteDescriptor())
                .Build());

        Assert.Contains(ex.Diagnostics, diagnostic => diagnostic.Code == "E-BINDING-CAP");
    }

    [Fact]
    public void ModuleValidator_rejects_custom_catalog_pure_binding_with_capability()
    {
        var result = new ModuleValidator().Validate(
            ModuleCallingCustomWrite(),
            new CustomCatalog(CustomPureCapabilitySignature()),
            PurePolicyWithTimeGrant());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "E-BINDING-EFFECT");
    }

    private static SandboxModule ModuleCallingCustomWrite()
        => new(
            "custom-catalog-capability-validation",
            SemVersion.One,
            SandboxLanguage.CurrentVersion,
            [],
            [
                new SandboxFunction(
                    "main",
                    IsEntrypoint: true,
                    [],
                    SandboxType.Unit,
                    [
                        new ExpressionStatement(new CallExpression("custom.write", [], null, Span), Span),
                        new ReturnStatement(new LiteralExpression(SandboxValue.Unit, Span), Span)
                    ])
            ],
            new Dictionary<string, string>());

    private static SandboxPolicy HostStateWritePolicy()
        => new(
            "custom-catalog-capability-validation",
            SandboxEffect.Cpu | SandboxEffect.HostStateWrite | SandboxEffect.Audit,
            [],
            new ResourceLimits());

    private static SandboxPolicy PurePolicyWithTimeGrant()
        => new(
            "custom-catalog-capability-validation",
            SandboxEffects.Pure,
            [new CapabilityGrant("time.now", new Dictionary<string, string>())],
            new ResourceLimits());

    private static BindingSignature CustomWriteSignature()
        => new(
            "custom.write",
            SemVersion.One,
            [],
            SandboxType.Unit,
            SandboxEffect.Cpu | SandboxEffect.HostStateWrite | SandboxEffect.Audit,
            RequiredCapability: null,
            BindingCostModel.Fixed(1),
            AuditLevel.PerCall,
            BindingSafety.SideEffectingExternal,
            CompiledBinding.RuntimeStub("Probe", "Write"));

    private static BindingSignature CustomPureCapabilitySignature()
        => new(
            "custom.write",
            SemVersion.One,
            [],
            SandboxType.Unit,
            SandboxEffects.Pure,
            RequiredCapability: "time.now",
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            CompiledBinding.RuntimeStub("Probe", "Write"));

    private static BindingDescriptor CustomWriteDescriptor()
        => new(
            "custom.write",
            SemVersion.One,
            [],
            SandboxType.Unit,
            SandboxEffect.Cpu | SandboxEffect.HostStateWrite | SandboxEffect.Audit,
            RequiredCapability: null,
            BindingCostModel.Fixed(1),
            AuditLevel.PerCall,
            BindingSafety.SideEffectingExternal,
            (_, _, _) => ValueTask.FromResult(SandboxValue.Unit),
            CompiledBinding.RuntimeStub(typeof(Kernels.Runtime.CompiledRuntime).FullName!, nameof(Kernels.Runtime.CompiledRuntime.CallBinding)));

    private sealed class CustomCatalog(BindingSignature signature) : IBindingCatalog
    {
        public IReadOnlyList<BindingSignature> Signatures { get; } = [signature];
        public string ManifestHash => "custom-catalog";

        public bool TryGet(string id, out BindingSignature binding)
        {
            if (StringComparer.Ordinal.Equals(id, signature.Id))
            {
                binding = signature;
                return true;
            }

            binding = null!;
            return false;
        }

        public bool Contains(string id)
            => StringComparer.Ordinal.Equals(id, signature.Id);

        public bool TryGetCapabilityGrantValidator(string capabilityId, out CapabilityGrantValidator validator)
        {
            validator = null!;
            return false;
        }
    }
}
