using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Validation;

namespace DotBoxD.Kernels.Tests.Validation;

public sealed class ModuleValidatorCustomCatalogAuditValidationTests
{
    private const string BindingId = "custom.log";
    private static readonly SourceSpan Span = new(0, 0);

    [Fact]
    public void ModuleValidator_rejects_custom_catalog_binding_with_reserved_audit_kind()
    {
        var result = new ModuleValidator().Validate(
            ModuleCallingCustomBinding(),
            new SingleBindingCatalog(BindingWithReservedAuditKind().Signature),
            SandboxPolicyBuilder.Create().GrantLogging().Build());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Code == "E-BINDING-AUDIT");
    }

    [Fact]
    public void BindingRegistry_rejects_equivalent_descriptor_with_reserved_audit_kind()
    {
        var ex = Assert.Throws<SandboxValidationException>(
            () => new BindingRegistryBuilder().Add(BindingWithReservedAuditKind()).Build());

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-BINDING-AUDIT");
    }

    private static SandboxModule ModuleCallingCustomBinding()
        => new(
            "module-validator-custom-catalog-audit-validation",
            SemVersion.One,
            SandboxLanguage.CurrentVersion,
            [new CapabilityRequest("log.write", "test log binding")],
            [
                new SandboxFunction(
                    "main",
                    IsEntrypoint: true,
                    [],
                    SandboxType.Unit,
                    [
                        new ExpressionStatement(
                            new CallExpression(
                                BindingId,
                                [new LiteralExpression(SandboxValue.FromString("message"), Span)],
                                null,
                                Span),
                            Span),
                        new ReturnStatement(new LiteralExpression(SandboxValue.Unit, Span), Span)
                    ])
            ],
            new Dictionary<string, string>());

    private static BindingDescriptor BindingWithReservedAuditKind()
        => new(
            BindingId,
            SemVersion.One,
            [SandboxType.String],
            SandboxType.Unit,
            SandboxEffect.Cpu | SandboxEffect.Audit,
            "log.write",
            BindingCostModel.Fixed(1),
            AuditLevel.PerCall,
            BindingSafety.SideEffectingExternal,
            (_, _, _) => ValueTask.FromResult(SandboxValue.Unit),
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)))
        {
            AuditKind = "RunSummary"
        };

    private sealed class SingleBindingCatalog(BindingSignature signature) : IBindingCatalog
    {
        public IReadOnlyList<BindingSignature> Signatures { get; } = [signature];

        public string ManifestHash => "custom-catalog-audit-validation";

        public bool Contains(string id)
            => string.Equals(id, signature.Id, StringComparison.Ordinal);

        public bool TryGet(string id, out BindingSignature binding)
        {
            if (Contains(id))
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
