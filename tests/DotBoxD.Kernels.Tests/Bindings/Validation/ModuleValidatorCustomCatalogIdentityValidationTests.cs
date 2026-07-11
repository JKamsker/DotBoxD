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
    private const string InvalidLowercaseBindingId = "system.IO.File.ReadAllText";
    private const string InvalidHostClrBindingId = "Host.System.IO.File.ReadAllText";
    private const string HostBindingId = "host.Regression.Game.InventoryService.ReadAllText";
    private static readonly SourceSpan Span = new(0, 0);

    [Theory]
    [InlineData(InvalidBindingId)]
    [InlineData(InvalidLowercaseBindingId)]
    [InlineData(InvalidHostClrBindingId)]
    public void ModuleValidator_rejects_custom_catalog_binding_with_invalid_identity(string invalidBindingId)
    {
        var result = new ModuleValidator().Validate(ModuleCallingLookupBinding(), CustomCatalog(invalidBindingId));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Code == "E-BINDING-ID");
    }

    [Theory]
    [InlineData(InvalidBindingId)]
    [InlineData(InvalidLowercaseBindingId)]
    [InlineData(InvalidHostClrBindingId)]
    public void BindingRegistry_rejects_equivalent_binding_descriptor_with_invalid_identity(string invalidBindingId)
    {
        var ex = Assert.Throws<SandboxValidationException>(() =>
            new BindingRegistryBuilder()
                .Add(InvalidIdentityDescriptor(invalidBindingId))
                .Build());

        Assert.Contains(ex.Diagnostics, d => d.Code == "E-BINDING-ID");
    }

    [Fact]
    public void ModuleValidator_accepts_host_catalog_binding_with_clr_shaped_segments()
    {
        var result = new ModuleValidator().Validate(ModuleCallingHostBinding(), HostCatalog());

        Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.Message)));
    }

    [Fact]
    public void BindingRegistry_accepts_host_descriptor_with_clr_shaped_segments()
    {
        var registry = new BindingRegistryBuilder()
            .Add(HostIdentityDescriptor())
            .Build();

        Assert.True(registry.TryGet(HostBindingId, out _));
    }

    private static SandboxModule ModuleCallingLookupBinding()
        => ModuleCalling(LookupBindingId, "custom-catalog-invalid-identity");

    private static SandboxModule ModuleCallingHostBinding()
        => ModuleCalling(HostBindingId, "custom-catalog-host-identity");

    private static SandboxModule ModuleCalling(string bindingId, string moduleId)
        => new(
            moduleId,
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
                            new CallExpression(bindingId, [], null, Span),
                            Span)
                    ])
            ],
            new Dictionary<string, string>());

    private static IBindingCatalog CustomCatalog(string invalidBindingId)
        => new SingleBindingCatalog(LookupBindingId, InvalidIdentitySignature(invalidBindingId));

    private static IBindingCatalog HostCatalog()
        => new SingleBindingCatalog(HostBindingId, HostIdentitySignature());

    private static BindingSignature InvalidIdentitySignature(string invalidBindingId)
    {
        var metadata = InvalidIdentityMetadata(invalidBindingId);
        return new(
            metadata.Id,
            metadata.Version,
            metadata.Parameters,
            metadata.ReturnType,
            metadata.Effects,
            metadata.RequiredCapability,
            metadata.CostModel,
            metadata.AuditLevel,
            metadata.Safety,
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));
    }

    private static BindingDescriptor InvalidIdentityDescriptor(string invalidBindingId)
    {
        var metadata = InvalidIdentityMetadata(invalidBindingId);
        return new(
            metadata.Id,
            metadata.Version,
            metadata.Parameters,
            metadata.ReturnType,
            metadata.Effects,
            metadata.RequiredCapability,
            metadata.CostModel,
            metadata.AuditLevel,
            metadata.Safety,
            (_, _, _) => ValueTask.FromResult(SandboxValue.Unit),
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));
    }

    private static BindingIdentityMetadata InvalidIdentityMetadata(string invalidBindingId)
        => new(
            invalidBindingId,
            SemVersion.One,
            [],
            SandboxType.Unit,
            SandboxEffect.Cpu,
            RequiredCapability: null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade);

    private static BindingSignature HostIdentitySignature()
        => new(
            HostBindingId,
            SemVersion.One,
            [],
            SandboxType.Unit,
            SandboxEffect.Cpu,
            null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureHostFacade,
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));

    private static BindingDescriptor HostIdentityDescriptor()
        => new(
            HostBindingId,
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

    private readonly record struct BindingIdentityMetadata(
        string Id,
        SemVersion Version,
        IReadOnlyList<SandboxType> Parameters,
        SandboxType ReturnType,
        SandboxEffect Effects,
        string? RequiredCapability,
        BindingCostModel CostModel,
        AuditLevel AuditLevel,
        BindingSafety Safety);

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
