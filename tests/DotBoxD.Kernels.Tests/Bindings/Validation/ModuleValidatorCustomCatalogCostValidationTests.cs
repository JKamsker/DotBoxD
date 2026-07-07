using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Validation;

namespace DotBoxD.Kernels.Tests.Bindings.Validation;

public sealed class ModuleValidatorCustomCatalogCostValidationTests
{
    private const string BindingId = "custom.pure";

    [Fact]
    public void ModuleValidator_rejects_custom_catalog_binding_with_negative_cost_model()
    {
        var result = new ModuleValidator().Validate(
            ModuleCallingCustomBinding(),
            new CustomCatalog(Signature(new BindingCostModel(BaseFuel: -1))));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "E-BINDING-COST");
    }

    [Fact]
    public void BindingRegistryBuilder_rejects_equivalent_negative_cost_model()
    {
        var ex = Assert.Throws<SandboxValidationException>(() =>
            new BindingRegistryBuilder()
                .Add(Descriptor(new BindingCostModel(BaseFuel: -1)))
                .Build());

        Assert.Contains(ex.Diagnostics, diagnostic => diagnostic.Code == "E-BINDING-COST");
    }

    private static SandboxModule ModuleCallingCustomBinding()
        => new(
            "custom-cost-module",
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
                        new ReturnStatement(
                            new CallExpression(BindingId, [], null, new SourceSpan(0, 0)),
                            new SourceSpan(0, 0))
                    ])
            ],
            new Dictionary<string, string>());

    private static BindingSignature Signature(BindingCostModel costModel)
        => new(
            BindingId,
            SemVersion.One,
            [],
            SandboxType.Unit,
            SandboxEffect.Cpu,
            null,
            costModel,
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            CompiledBinding.RuntimeStub(
                typeof(CompiledRuntime).FullName!,
                nameof(CompiledRuntime.CallBinding)));

    private static BindingDescriptor Descriptor(BindingCostModel costModel)
    {
        var signature = Signature(costModel);
        return new BindingDescriptor(
            signature.Id,
            signature.Version,
            signature.Parameters,
            signature.ReturnType,
            signature.Effects,
            signature.RequiredCapability,
            signature.CostModel,
            signature.AuditLevel,
            signature.Safety,
            (_, _, _) => ValueTask.FromResult(SandboxValue.Unit),
            signature.Compiled);
    }

    private sealed class CustomCatalog(BindingSignature signature) : IBindingCatalog
    {
        public IReadOnlyList<BindingSignature> Signatures { get; } = [signature];

        public string ManifestHash => "custom-cost-catalog";

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

        public bool Contains(string id) => StringComparer.Ordinal.Equals(id, signature.Id);

        public bool TryGetCapabilityGrantValidator(string capabilityId, out CapabilityGrantValidator validator)
        {
            validator = null!;
            return false;
        }
    }
}
