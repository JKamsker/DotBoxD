using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Bindings.Validation;

public sealed class BindingReferenceCollectorValidationTests
{
    private const string BindingId = "test.read";
    private static readonly SourceSpan Span = new(0, 0);

    [Fact]
    public void Collect_rejects_null_module_argument()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            BindingReferenceCollector.Collect(null!, EmptyBindings()));

        Assert.Equal("module", ex.ParamName);
    }

    [Fact]
    public void CollectByFunction_rejects_null_module_argument()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            BindingReferenceCollector.CollectByFunction(null!, EmptyBindings()));

        Assert.Equal("module", ex.ParamName);
    }

    [Fact]
    public void Collect_rejects_null_bindings_argument()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            BindingReferenceCollector.Collect(PureModule(), null!));

        Assert.Equal("bindings", ex.ParamName);
    }

    [Fact]
    public void CollectByFunction_rejects_null_bindings_argument()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            BindingReferenceCollector.CollectByFunction(PureModule(), null!));

        Assert.Equal("bindings", ex.ParamName);
    }

    [Fact]
    public void Collect_rejects_unknown_entrypoint()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            BindingReferenceCollector.Collect(ModuleCallingKnownBinding(), KnownBindings(), "missing"));

        Assert.Equal("entrypoint", ex.ParamName);
    }

    [Fact]
    public void Collect_for_named_entrypoint_returns_referenced_binding()
    {
        var references = BindingReferenceCollector.Collect(ModuleCallingKnownBinding(), KnownBindings(), "main");

        Assert.Equal([BindingId], references);
    }

    private static BindingRegistry EmptyBindings() => new([]);

    private static IBindingCatalog KnownBindings() => new SingleBindingCatalog(KnownBinding());

    private static BindingSignature KnownBinding()
        => new(
            BindingId,
            SemVersion.One,
            [],
            SandboxType.Unit,
            SandboxEffect.Cpu,
            RequiredCapability: null,
            BindingCostModel.Fixed(1),
            AuditLevel.None,
            BindingSafety.PureIntrinsic,
            CompiledBinding.RuntimeStub("test", "read"));

    private static SandboxModule ModuleCallingKnownBinding()
        => new(
            "binding-module",
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

    private static SandboxModule PureModule()
        => new(
            "pure-module",
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
                            new LiteralExpression(SandboxValue.Unit, new SourceSpan(0, 0)),
                            new SourceSpan(0, 0))
                    ])
            ],
            new Dictionary<string, string>());

    private sealed class SingleBindingCatalog(BindingSignature binding) : IBindingCatalog
    {
        public IReadOnlyList<BindingSignature> Signatures { get; } = [binding];

        public string ManifestHash => "test-bindings";

        public bool Contains(string id) => id == binding.Id;

        public bool TryGet(string id, out BindingSignature found)
        {
            found = binding;
            return id == binding.Id;
        }

        public bool TryGetCapabilityGrantValidator(string capabilityId, out CapabilityGrantValidator validator)
        {
            validator = null!;
            return false;
        }
    }
}
