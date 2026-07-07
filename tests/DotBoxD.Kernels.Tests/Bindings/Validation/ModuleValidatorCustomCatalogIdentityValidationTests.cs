using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Validation;

namespace DotBoxD.Kernels.Tests.Bindings.Validation;

public sealed class ModuleValidatorCustomCatalogIdentityValidationTests
{
    private const string LookupBindingId = "safe.call";
    private const string InvalidBindingId = "System.IO.File.ReadAllText";
    private static readonly SourceSpan Span = new(0, 0);

    [Fact]
    public void ModuleValidator_rejects_custom_catalog_binding_with_invalid_identity()
    {
        var result = new ModuleValidator().Validate(ModuleCallingLookupBinding(), CustomCatalog());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Code == "E-BINDING-ID");
    }

    [Fact]
    public void BindingRegistry_rejects_equivalent_binding_descriptor_with_invalid_identity()
    {
        var ex = Assert.Throws<SandboxValidationException>(() =>
            new BindingRegistryBuilder()
                .Add(InvalidIdentityDescriptor())
                .Build());

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-BINDING-ID");
    }

    private static SandboxModule ModuleCallingLookupBinding()
        => new(
            "custom-catalog-invalid-identity",
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
                            new CallExpression(LookupBindingId, [], null, Span),
                            Span)
                    ])
            ],
            new Dictionary<string, string>());

    private static IBindingCatalog CustomCatalog()
        => new SingleBindingCatalog(LookupBindingId, InvalidIdentitySignature());

    private static BindingSignature InvalidIdentitySignature()
        => new(
            InvalidBindingId,
            SemVersion.One,
            [],
            SandboxType.Unit,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));

    private static BindingDescriptor InvalidIdentityDescriptor()
        => new(
            InvalidBindingId,
            SemVersion.One,
            [],
            SandboxType.Unit,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (_, _, _) => ValueTask.FromResult(SandboxValue.Unit),
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));

    private sealed class SingleBindingCatalog(string lookupId, BindingSignature binding) : IBindingCatalog
    {
        public IReadOnlyList<BindingSignature> Signatures { get; } = [binding];

        public string ManifestHash => "test-manifest";

        public bool TryGet(string id, out BindingSignature result)
        {
            if (id == lookupId)
            {
                result = binding;
                return true;
            }

            result = null!;
            return false;
        }

        public bool Contains(string id) => id == lookupId;

        public bool TryGetCapabilityGrantValidator(string capabilityId, out CapabilityGrantValidator validator)
        {
            validator = null!;
            return false;
        }
    }
}
