using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Validation;

namespace DotBoxD.Kernels.Tests.Bindings.Validation;

public sealed class ModuleValidatorCustomCatalogDangerousBindingTests
{
    private const string BindingId = "custom.danger";

    [Fact]
    public void ModuleValidator_rejects_custom_catalog_dangerous_binding()
    {
        var signature = DangerousBindingDescriptor().Signature;
        var catalog = new SingleBindingCatalog(signature);
        var module = ModuleCalling(BindingId);
        var policy = SandboxPolicyBuilder.Create().GrantLogging().Build();

        var result = new ModuleValidator().Validate(module, catalog, policy);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Code == "E-BINDING-DANGER");
    }

    [Fact]
    public void BindingRegistry_rejects_equivalent_dangerous_binding()
    {
        var ex = Assert.Throws<SandboxValidationException>(() =>
            new BindingRegistryBuilder().Add(DangerousBindingDescriptor()).Build());

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-BINDING-DANGER");
    }

    private static BindingDescriptor DangerousBindingDescriptor()
        => new(
            BindingId,
            SemVersion.One,
            [],
            SandboxType.Unit,
            SandboxEffect.Cpu | SandboxEffect.Audit,
            "log.write",
            BindingCostModel.Fixed(1),
            AuditLevel.PerCall,
            BindingSafety.DangerousRequiresReview,
            (_, _, _) => ValueTask.FromResult(SandboxValue.Unit),
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));

    private static SandboxModule ModuleCalling(string bindingId)
        => new(
            "custom-catalog-dangerous-binding",
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
                            new CallExpression(bindingId, [], GenericType: null, new SourceSpan(0, 0)),
                            new SourceSpan(0, 0))
                    ])
            ],
            new Dictionary<string, string>());

    private sealed class SingleBindingCatalog(BindingSignature binding) : IBindingCatalog
    {
        public IReadOnlyList<BindingSignature> Signatures { get; } = [binding];

        public string ManifestHash => "custom-catalog-dangerous-binding";

        public bool Contains(string id) => string.Equals(id, binding.Id, StringComparison.Ordinal);

        public bool TryGet(string id, out BindingSignature result)
        {
            if (Contains(id))
            {
                result = binding;
                return true;
            }

            result = null!;
            return false;
        }

        public bool TryGetCapabilityGrantValidator(string capabilityId, out CapabilityGrantValidator validator)
        {
            validator = null!;
            return false;
        }
    }
}
