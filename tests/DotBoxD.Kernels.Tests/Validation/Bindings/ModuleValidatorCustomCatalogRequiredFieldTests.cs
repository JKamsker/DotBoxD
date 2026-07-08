using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Validation;
using DotBoxD.Kernels.Validation.Model;

namespace DotBoxD.Kernels.Tests.Validation;

public sealed class ModuleValidatorCustomCatalogRequiredFieldTests
{
    private const string BindingId = "custom.required";
    private static readonly SourceSpan Span = new(0, 0);

    [Theory]
    [InlineData("Version", "Version")]
    [InlineData("ReturnType", "ReturnType")]
    [InlineData("CostModel", "CostModel")]
    [InlineData("Compiled", "Compiled")]
    [InlineData("ParameterElement", "Parameters")]
    [InlineData("Id", "Id")]
    public void ModuleValidator_reports_required_field_diagnostic_for_malformed_custom_catalog_binding(
        string malformedField,
        string expectedField)
    {
        ModuleValidationResult? result = null;

        var exception = Record.Exception(() =>
            result = new ModuleValidator().Validate(
                ModuleCallingCustomBinding(),
                new SingleBindingCatalog(MalformedSignature(malformedField))));

        Assert.Null(exception);
        Assert.NotNull(result);
        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "E-BINDING-REQUIRED" &&
            diagnostic.Message.Contains("binding descriptor", StringComparison.OrdinalIgnoreCase) &&
            diagnostic.Message.Contains(expectedField, StringComparison.Ordinal));
    }

    private static SandboxModule ModuleCallingCustomBinding()
        => new(
            "module-validator-custom-catalog-required-field-validation",
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
                        new ExpressionStatement(new CallExpression(BindingId, [], null, Span), Span),
                        new ReturnStatement(new LiteralExpression(SandboxValue.Unit, Span), Span)
                    ])
            ],
            new Dictionary<string, string>());

    private static BindingSignature MalformedSignature(string field)
    {
        var signature = ValidSignature();
        return field switch
        {
            "Id" => signature with { Id = null! },
            "Version" => signature with { Version = null! },
            "ReturnType" => signature with { ReturnType = null! },
            "CostModel" => signature with { CostModel = null! },
            "Compiled" => signature with { Compiled = null! },
            "ParameterElement" => signature with { Parameters = [null!] },
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Unknown malformed field.")
        };
    }

    private static BindingSignature ValidSignature()
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
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));

    private sealed class SingleBindingCatalog(BindingSignature signature) : IBindingCatalog
    {
        public IReadOnlyList<BindingSignature> Signatures { get; } = [signature];
        public string ManifestHash => "custom-catalog-required-field-validation";

        public bool Contains(string id)
            => StringComparer.Ordinal.Equals(id, signature.Id);

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
