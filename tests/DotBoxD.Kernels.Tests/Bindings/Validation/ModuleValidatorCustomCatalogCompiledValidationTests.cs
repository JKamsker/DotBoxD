using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Validation;

namespace DotBoxD.Kernels.Tests.Bindings.Validation;

public sealed class ModuleValidatorCustomCatalogCompiledValidationTests
{
    private const string BindingId = "custom.read";
    private static readonly SourceSpan Span = new(0, 0);

    [Fact]
    public void ModuleValidator_rejects_custom_catalog_binding_with_unsafe_compiled_target()
    {
        var result = new ModuleValidator().Validate(
            ModuleCallingCustomBinding(),
            new SingleBindingCatalog(UnsafeCompiledSignature()));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Code == "E-BINDING-COMPILED");
    }

    [Fact]
    public void BindingRegistry_rejects_equivalent_unsafe_compiled_target()
    {
        var ex = Assert.Throws<SandboxValidationException>(() =>
            new BindingRegistryBuilder()
                .Add(UnsafeCompiledDescriptor())
                .Build());

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-BINDING-COMPILED");
    }

    private static SandboxModule ModuleCallingCustomBinding()
        => new(
            "custom-compiled-target",
            SemVersion.One,
            SandboxLanguage.CurrentVersion,
            [],
            [
                new SandboxFunction(
                    "main",
                    IsEntrypoint: true,
                    [],
                    SandboxType.Unit,
                    [new ReturnStatement(new CallExpression(BindingId, [], null, Span), Span)])
            ],
            new Dictionary<string, string>());

    private static BindingDescriptor UnsafeCompiledDescriptor()
        => new(
            BindingId,
            SemVersion.One,
            [],
            SandboxType.Unit,
            SandboxEffect.Cpu,
            RequiredCapability: null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (_, _, _) => ValueTask.FromResult(SandboxValue.Unit),
            CompiledBinding.RuntimeStub("System.IO.File", "ReadAllText"));

    private static BindingSignature UnsafeCompiledSignature()
        => UnsafeCompiledDescriptor().Signature;

    private sealed class SingleBindingCatalog(BindingSignature signature) : IBindingCatalog
    {
        public IReadOnlyList<BindingSignature> Signatures { get; } = [signature];

        public string ManifestHash => "custom-catalog";

        public bool Contains(string id) => id == signature.Id;

        public bool TryGet(string id, out BindingSignature binding)
        {
            if (id == signature.Id)
            {
                binding = signature;
                return true;
            }

            binding = null!;
            return false;
        }

        public bool TryGetCapabilityGrantValidator(string capabilityId, out CapabilityGrantValidator validator)
        {
            validator = null!;
            return false;
        }
    }
}
