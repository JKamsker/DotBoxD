using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Validation;

namespace DotBoxD.Kernels.Tests.Bindings.Validation.CustomCatalog;

public sealed class ModuleValidatorCustomCatalogTypeValidationTests
{
    private const string BindingId = "custom.unsafeType";
    private static readonly SourceSpan Span = new(0, 0);

    [Fact]
    public void ModuleValidator_rejects_custom_catalog_binding_with_forbidden_return_type()
    {
        var signature = UnsafeTypeSignature();
        var result = new ModuleValidator().Validate(ModuleCalling(BindingId), new CustomCatalog(signature));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Code == "E-BINDING-TYPE");
    }

    [Fact]
    public void BindingRegistry_rejects_equivalent_binding_descriptor_with_forbidden_return_type()
    {
        var ex = Assert.Throws<SandboxValidationException>(() =>
            new BindingRegistryBuilder().Add(UnsafeTypeDescriptor()).Build());

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-BINDING-TYPE");
    }

    private static SandboxModule ModuleCalling(string bindingId)
        => new(
            "custom-type-module",
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
                        new ExpressionStatement(new CallExpression(bindingId, [], null, Span), Span),
                        new ReturnStatement(new LiteralExpression(SandboxValue.Unit, Span), Span)
                    ])
            ],
            new Dictionary<string, string>());

    private static BindingSignature UnsafeTypeSignature()
        => UnsafeTypeDescriptor().Signature;

    private static BindingDescriptor UnsafeTypeDescriptor()
        => new(
            BindingId,
            SemVersion.One,
            [],
            SandboxType.Scalar("System.String"),
            SandboxEffect.Cpu,
            RequiredCapability: null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            (_, _, _) => ValueTask.FromResult(SandboxValue.Unit),
            CompiledBinding.RuntimeStub(
                typeof(CompiledRuntime).FullName!,
                nameof(CompiledRuntime.CallBinding)));

    private sealed class CustomCatalog(BindingSignature signature) : IBindingCatalog
    {
        public IReadOnlyList<BindingSignature> Signatures { get; } = [signature];

        public string ManifestHash => "custom";

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
