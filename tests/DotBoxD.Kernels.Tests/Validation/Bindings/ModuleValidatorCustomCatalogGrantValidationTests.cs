using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Validation;

namespace DotBoxD.Kernels.Tests.Validation;

public sealed class ModuleValidatorCustomCatalogGrantValidationTests
{
    private const string BindingId = "custom.read";
    private const string CapabilityId = "custom.data.read";

    [Fact]
    public void ModuleValidator_rejects_custom_catalog_capability_without_grant_validator()
    {
        var binding = CustomBindingSignature();
        var catalog = new CustomCatalog(binding);
        var policy = CustomGrantPolicy();

        var result = new ModuleValidator().Validate(ModuleCallingCustomBinding(), catalog, policy);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "E-BINDING-GRANT");
    }

    [Fact]
    public void BindingRegistry_rejects_equivalent_descriptor_without_grant_validator()
    {
        var descriptor = CustomBindingDescriptor();

        var ex = Assert.Throws<SandboxValidationException>(() =>
            new BindingRegistryBuilder().Add(descriptor).Build());

        Assert.Contains(ex.Diagnostics, diagnostic => diagnostic.Code == "E-BINDING-GRANT");
    }

    [Fact]
    public void ModuleValidator_accepts_custom_catalog_capability_with_grant_validator()
    {
        var binding = CustomBindingSignature();
        var catalog = new CustomCatalog(binding, static (_, _) => { });
        var policy = CustomGrantPolicy();

        var result = new ModuleValidator().Validate(ModuleCallingCustomBinding(), catalog, policy);

        Assert.True(result.Succeeded, string.Join(", ", result.Diagnostics.Select(d => $"{d.Code}: {d.Message}")));
    }

    private static BindingSignature CustomBindingSignature()
        => new(
            BindingId,
            SemVersion.One,
            [],
            SandboxType.Unit,
            SandboxEffect.HostStateRead | SandboxEffect.Audit,
            CapabilityId,
            BindingCostModel.Fixed(1),
            AuditLevel.PerCall,
            BindingSafety.ReadOnlyExternal,
            CompiledBinding.RuntimeStub(
                typeof(CompiledRuntime).FullName!,
                nameof(CompiledRuntime.CallBinding)));

    private static BindingDescriptor CustomBindingDescriptor()
        => new(
            BindingId,
            SemVersion.One,
            [],
            SandboxType.Unit,
            SandboxEffect.HostStateRead | SandboxEffect.Audit,
            CapabilityId,
            BindingCostModel.Fixed(1),
            AuditLevel.PerCall,
            BindingSafety.ReadOnlyExternal,
            (_, _, _) => ValueTask.FromResult(SandboxValue.Unit),
            CompiledBinding.RuntimeStub(
                typeof(CompiledRuntime).FullName!,
                nameof(CompiledRuntime.CallBinding)));

    private static SandboxPolicy CustomGrantPolicy()
        => new(
            "custom-grant-policy",
            SandboxEffects.Pure | SandboxEffect.HostStateRead | SandboxEffect.Audit,
            [
                new CapabilityGrant(
                    CapabilityId,
                    new Dictionary<string, string> { ["scope"] = "inventory" })
            ],
            new ResourceLimits(MaxFuel: 1_000));

    private static SandboxModule ModuleCallingCustomBinding()
        => new(
            "custom-catalog-grant-validation",
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
                            new CallExpression(BindingId, [], GenericType: null, new SourceSpan(0, 0)),
                            new SourceSpan(0, 0))
                    ])
            ],
            new Dictionary<string, string>());

    private sealed class CustomCatalog(
        BindingSignature binding,
        CapabilityGrantValidator? grantValidator = null) : IBindingCatalog
    {
        private readonly BindingSignature[] _signatures = [binding];

        public IReadOnlyList<BindingSignature> Signatures => _signatures;

        public string ManifestHash => "custom-catalog";

        public bool Contains(string id) => string.Equals(id, BindingId, StringComparison.Ordinal);

        public bool TryGet(string id, out BindingSignature binding)
        {
            if (Contains(id))
            {
                binding = _signatures[0];
                return true;
            }

            binding = default!;
            return false;
        }

        public bool TryGetCapabilityGrantValidator(string capabilityId, out CapabilityGrantValidator validator)
        {
            if (string.Equals(capabilityId, CapabilityId, StringComparison.Ordinal) &&
                grantValidator is not null)
            {
                validator = grantValidator;
                return true;
            }

            validator = default!;
            return false;
        }
    }
}
