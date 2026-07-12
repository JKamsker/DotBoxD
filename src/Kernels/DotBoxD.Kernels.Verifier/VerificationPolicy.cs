using DotBoxD.Kernels.Verifier.Generated;

namespace DotBoxD.Kernels.Verifier;

using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text;
using static DotBoxD.Kernels.Verifier.VerificationPolicyDefaults;
using static DotBoxD.Kernels.Verifier.VerifierTypeNames;

public sealed record VerificationPolicy(
    IReadOnlySet<string> AllowedAssemblies,
    IReadOnlySet<string> AllowedAssemblyIdentities,
    IReadOnlySet<string> AllowedTypes,
    IReadOnlySet<string> AllowedMembers,
    IReadOnlySet<string> ForbiddenTypePrefixes,
    IReadOnlySet<string> RuntimeFacadeIdentities,
    string VerifierVersion,
    VerificationManifestIdentity? ExpectedManifestIdentity = null)
{
    private IReadOnlySet<string> _allowedAssemblies = Freeze(AllowedAssemblies, nameof(AllowedAssemblies));
    private IReadOnlySet<string> _allowedAssemblyIdentities = Freeze(AllowedAssemblyIdentities, nameof(AllowedAssemblyIdentities));
    private IReadOnlySet<string> _allowedTypes = Freeze(AllowedTypes, nameof(AllowedTypes));
    private IReadOnlySet<string> _allowedMembers = Freeze(AllowedMembers, nameof(AllowedMembers));
    private IReadOnlySet<string> _forbiddenTypePrefixes = Freeze(ForbiddenTypePrefixes, nameof(ForbiddenTypePrefixes));
    private IReadOnlySet<string> _runtimeFacadeIdentities = Freeze(RuntimeFacadeIdentities, nameof(RuntimeFacadeIdentities));
    private string _verifierVersion = RequireVersion(VerifierVersion, nameof(VerifierVersion));
    private string? _allowlistHash;
    private string? _runtimeFacadeHash;

    public IReadOnlySet<string> AllowedAssemblies { get => _allowedAssemblies; init { _allowedAssemblies = Freeze(value, nameof(AllowedAssemblies)); InvalidateHashes(); } }
    public IReadOnlySet<string> AllowedAssemblyIdentities { get => _allowedAssemblyIdentities; init { _allowedAssemblyIdentities = Freeze(value, nameof(AllowedAssemblyIdentities)); InvalidateHashes(); } }
    public IReadOnlySet<string> AllowedTypes { get => _allowedTypes; init { _allowedTypes = Freeze(value, nameof(AllowedTypes)); InvalidateHashes(); } }
    public IReadOnlySet<string> AllowedMembers { get => _allowedMembers; init { _allowedMembers = Freeze(value, nameof(AllowedMembers)); InvalidateHashes(); } }
    public IReadOnlySet<string> ForbiddenTypePrefixes { get => _forbiddenTypePrefixes; init { _forbiddenTypePrefixes = Freeze(value, nameof(ForbiddenTypePrefixes)); InvalidateHashes(); } }
    public IReadOnlySet<string> RuntimeFacadeIdentities { get => _runtimeFacadeIdentities; init { _runtimeFacadeIdentities = Freeze(value, nameof(RuntimeFacadeIdentities)); InvalidateHashes(); } }
    public string VerifierVersion { get => _verifierVersion; init => _verifierVersion = RequireVersion(value, nameof(VerifierVersion)); }

    public static VerificationPolicy BoxedValueDefaults()
        => new(
            new HashSet<string>(StringComparer.Ordinal) {
                "System.Private.CoreLib", "System.Runtime", "DotBoxD.Kernels", "DotBoxD.Kernels.Runtime"
            },
            new HashSet<string>(StringComparer.Ordinal) {
                AssemblyIdentity("System.Private.CoreLib", "10.0.0.0", "neutral", "7cec85d7bea7798e"),
                AssemblyIdentity("System.Runtime", "10.0.0.0", "neutral", "b03f5f7f11d50a3a"),
                AssemblyIdentity("DotBoxD.Kernels", DotBoxDAssemblyVersion(), "neutral", "null"),
                AssemblyIdentity("DotBoxD.Kernels.Runtime", DotBoxDAssemblyVersion(), "neutral", "null")
            },
            new HashSet<string>(StringComparer.Ordinal) {
                ObjectName, VoidName, BooleanName, Int32Name, Int64Name, StringName, DoubleName,
                SandboxValueName, SandboxContextName, SandboxTypeName, CompiledRuntimeName
            },
            new HashSet<string>(StringComparer.Ordinal) {
                RuntimeMember("ChargeFuel", $"{SandboxContextName},{Int32Name}", VoidName),
                RuntimeMember("ChargeLoopIteration", $"{SandboxContextName},{Int32Name}", VoidName),
                RuntimeMember("AccumulateLinearI32", $"{SandboxContextName},{Int32Name},{Int32Name},{Int32Name},{Int32Name}", Int32Name),
                RuntimeMember("CanBulkChargeLoopIterations", $"{SandboxContextName},{Int32Name},{Int32Name}", BooleanName),
                RuntimeMember("CanBulkChargeLoopIterationsAndFuel", $"{SandboxContextName},{Int32Name},{Int32Name},{Int64Name}", BooleanName),
                RuntimeMember("StringEqualsRaw", $"{SandboxValueName},{SandboxValueName}", BooleanName),
                RuntimeMember("GteI32Raw", $"{Int32Name},{Int32Name}", BooleanName),
                RuntimeMember("CanBulkChargeFuel", $"{SandboxContextName},{Int64Name},{Int32Name}", BooleanName),
                RuntimeMember("ChargeBulkFuel", $"{SandboxContextName},{Int64Name},{Int32Name}", VoidName),
                RuntimeMember("ChargeFuel64", $"{SandboxContextName},{Int64Name}", VoidName),
                RuntimeMember("ChargeSandboxValue", $"{SandboxContextName},{SandboxValueName}", VoidName),
                RuntimeMember("CanBulkChargeSandboxValue", $"{SandboxContextName},{SandboxValueName},{Int32Name}", BooleanName),
                RuntimeMember("ChargeSandboxValues", $"{SandboxContextName},{SandboxValueName},{Int32Name}", VoidName),
                RuntimeMember("ChargeBindingCall", $"{SandboxContextName},{StringName}", VoidName),
                RuntimeMember("CanBulkChargeBindingCalls", $"{SandboxContextName},{StringName},{Int32Name}", BooleanName),
                RuntimeMember("ChargeBindingCalls", $"{SandboxContextName},{StringName},{Int32Name}", VoidName),
                RuntimeMember("CanBulkChargeBindingCallsScaled", $"{SandboxContextName},{StringName},{Int32Name},{Int32Name}", BooleanName),
                RuntimeMember("ChargeBindingCallsScaled", $"{SandboxContextName},{StringName},{Int32Name},{Int32Name}", VoidName),
                RuntimeMember("EnterCall", SandboxContextName, VoidName),
                RuntimeMember("ExitCall", SandboxContextName, VoidName),
                RuntimeMember("EnterInlineCall", SandboxContextName, VoidName),
                RuntimeMember("ExitInlineCall", SandboxContextName, VoidName),
                RuntimeMember("ValidateEntrypointInput", $"{SandboxValueName},{Int32Name}", VoidName),
                RuntimeMember("GetInputArgument", $"{SandboxValueName},{Int32Name},{Int32Name},{SandboxTypeName}", SandboxValueName),
                RuntimeMember("RequireValueType", $"{SandboxValueName},{SandboxTypeName}", SandboxValueName),
                RuntimeMember("Unit", "", SandboxValueName),
                RuntimeMember("I32", Int32Name, SandboxValueName),
                RuntimeMember("I64", Int64Name, SandboxValueName),
                RuntimeMember("F64", DoubleName, SandboxValueName),
                RuntimeMember("Bool", BooleanName, SandboxValueName),
                RuntimeMember("TypeScalar", StringName, SandboxTypeName),
                RuntimeMember("TypeList", SandboxTypeName, SandboxTypeName),
                RuntimeMember("TypeMap", $"{SandboxTypeName},{SandboxTypeName}", SandboxTypeName),
                RuntimeMember("TypeRecord", SandboxTypeArrayName, SandboxTypeName),
                RuntimeMember("CreateMeteredTypeArray", $"{SandboxContextName},{Int32Name}", SandboxTypeArrayName),
                RuntimeMember("StringConst", $"{SandboxContextName},{StringName}", SandboxValueName),
                RuntimeMember("OpaqueIdConst", $"{SandboxContextName},{StringName},{StringName}", SandboxValueName),
                RuntimeMember("PathConst", $"{SandboxContextName},{StringName}", SandboxValueName),
                RuntimeMember("UriConst", $"{SandboxContextName},{StringName}", SandboxValueName),
                RuntimeMember("StringLiteralValue", StringName, SandboxValueName),
                RuntimeMember("OpaqueIdLiteralValue", $"{StringName},{StringName}", SandboxValueName),
                RuntimeMember("GuidLiteralValue", StringName, SandboxValueName),
                RuntimeMember("PathLiteralValue", StringName, SandboxValueName),
                RuntimeMember("UriLiteralValue", StringName, SandboxValueName),
                RuntimeMember("AsI32", SandboxValueName, Int32Name),
                RuntimeMember("AsI64", SandboxValueName, Int64Name),
                RuntimeMember("AsBool", SandboxValueName, BooleanName),
                RuntimeMember("AsF64", SandboxValueName, DoubleName),
                RuntimeMember("AddI32Raw", $"{Int32Name},{Int32Name}", Int32Name),
                RuntimeMember("SubI32Raw", $"{Int32Name},{Int32Name}", Int32Name),
                RuntimeMember("MulI32Raw", $"{Int32Name},{Int32Name}", Int32Name),
                RuntimeMember("DivI32Raw", $"{Int32Name},{Int32Name}", Int32Name),
                RuntimeMember("RemI32Raw", $"{Int32Name},{Int32Name}", Int32Name),
                RuntimeMember("AddRemI32Raw", $"{Int32Name},{Int32Name},{Int32Name}", Int32Name),
                RuntimeMember("NegI32Raw", Int32Name, Int32Name),
                RuntimeMember("AbsI32Raw", Int32Name, Int32Name),
                RuntimeMember("MinI32Raw", $"{Int32Name},{Int32Name}", Int32Name),
                RuntimeMember("MaxI32Raw", $"{Int32Name},{Int32Name}", Int32Name),
                RuntimeMember("ClampI32Raw", $"{Int32Name},{Int32Name},{Int32Name}", Int32Name),
                RuntimeMember("AddF64Raw", $"{DoubleName},{DoubleName}", DoubleName),
                RuntimeMember("SubF64Raw", $"{DoubleName},{DoubleName}", DoubleName),
                RuntimeMember("MulF64Raw", $"{DoubleName},{DoubleName}", DoubleName),
                RuntimeMember("DivF64Raw", $"{DoubleName},{DoubleName}", DoubleName),
                RuntimeMember("LtI32Raw", $"{Int32Name},{Int32Name}", BooleanName),
                RuntimeMember("LteI32Raw", $"{Int32Name},{Int32Name}", BooleanName),
                RuntimeMember("GtI32Raw", $"{Int32Name},{Int32Name}", BooleanName),
                RuntimeMember("GteI32Raw", $"{Int32Name},{Int32Name}", BooleanName),
                RuntimeMember("EqI32Raw", $"{Int32Name},{Int32Name}", BooleanName),
                RuntimeMember("NeI32Raw", $"{Int32Name},{Int32Name}", BooleanName),
                RuntimeMember("AddI64Raw", $"{Int64Name},{Int64Name}", Int64Name),
                RuntimeMember("SubI64Raw", $"{Int64Name},{Int64Name}", Int64Name),
                RuntimeMember("MulI64Raw", $"{Int64Name},{Int64Name}", Int64Name),
                RuntimeMember("DivI64Raw", $"{Int64Name},{Int64Name}", Int64Name),
                RuntimeMember("RemI64Raw", $"{Int64Name},{Int64Name}", Int64Name),
                RuntimeMember("NegI64Raw", Int64Name, Int64Name),
                RuntimeMember("LtF64Raw", $"{DoubleName},{DoubleName}", BooleanName),
                RuntimeMember("LteF64Raw", $"{DoubleName},{DoubleName}", BooleanName),
                RuntimeMember("GtF64Raw", $"{DoubleName},{DoubleName}", BooleanName),
                RuntimeMember("GteF64Raw", $"{DoubleName},{DoubleName}", BooleanName),
                RuntimeMember("EqF64Raw", $"{DoubleName},{DoubleName}", BooleanName),
                RuntimeMember("NeF64Raw", $"{DoubleName},{DoubleName}", BooleanName),
                RuntimeMember("LtI64Raw", $"{Int64Name},{Int64Name}", BooleanName),
                RuntimeMember("LteI64Raw", $"{Int64Name},{Int64Name}", BooleanName),
                RuntimeMember("GtI64Raw", $"{Int64Name},{Int64Name}", BooleanName),
                RuntimeMember("GteI64Raw", $"{Int64Name},{Int64Name}", BooleanName),
                RuntimeMember("EqI64Raw", $"{Int64Name},{Int64Name}", BooleanName),
                RuntimeMember("NeI64Raw", $"{Int64Name},{Int64Name}", BooleanName),
                RuntimeMember("AddI32", $"{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("SubI32", $"{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("MulI32", $"{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("DivI32", $"{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("RemI32", $"{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("NegI32", SandboxValueName, SandboxValueName),
                RuntimeMember("Neg", SandboxValueName, SandboxValueName),
                RuntimeMember("Add", $"{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("Sub", $"{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("Mul", $"{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("Div", $"{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("Rem", $"{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("NotBool", SandboxValueName, SandboxValueName),
                RuntimeMember("Eq", $"{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("Ne", $"{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("LtI32", $"{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("LteI32", $"{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("GtI32", $"{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("GteI32", $"{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("Lt", $"{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("Lte", $"{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("Gt", $"{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("Gte", $"{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("And", $"{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("Or", $"{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("Int32ToStringInvariant", $"{SandboxContextName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("StringLength", SandboxValueName, SandboxValueName),
                RuntimeMember("StringLengthRaw", SandboxValueName, Int32Name),
                RuntimeMember("ListCountRaw", SandboxValueName, Int32Name),
                RuntimeMember("ListReadFuelRaw", Int32Name, Int64Name),
                RuntimeMember("ListGetI32Raw", $"{SandboxValueName},{Int32Name}", Int32Name),
                RuntimeMember("CreateMeteredListI32ReaderRaw", $"{SandboxContextName},{SandboxValueName}", ObjectName),
                RuntimeMember("ListI32ReaderGetRaw", $"{ObjectName},{Int32Name}", Int32Name),
                RuntimeMember("ListI32ReaderGetRemainderRaw", $"{ObjectName},{Int32Name},{Int32Name}", Int32Name),
                RuntimeMember("ListI32ReaderAddRemainderCycleFromZeroRaw", $"{SandboxContextName},{ObjectName},{Int32Name},{Int32Name},{Int32Name},{Int32Name},{Int64Name}", Int32Name),
                RuntimeMember("CanUseModuloBranchAccumulatorRaw", $"{SandboxContextName},{Int32Name},{Int32Name},{Int32Name},{Int32Name},{Int32Name},{Int32Name},{Int32Name},{Int32Name},{Int32Name}", BooleanName),
                RuntimeMember("AddModuloBranchDeltasI32LoopRaw", $"{SandboxContextName},{Int32Name},{Int32Name},{Int32Name},{Int32Name},{Int32Name},{Int32Name},{Int32Name},{Int32Name},{Int32Name}", Int32Name),
                RuntimeMember("CanUseModuloIndexAccumulatorRaw", $"{SandboxContextName},{Int32Name},{Int32Name},{Int32Name},{Int32Name},{Int32Name},{Int32Name}", BooleanName),
                RuntimeMember("AddModuloIndexAccumulatorI32LoopRaw", $"{SandboxContextName},{Int32Name},{Int32Name},{Int32Name},{Int32Name},{Int32Name},{Int32Name}", Int32Name),
                RuntimeMember("MapCountRaw", SandboxValueName, Int32Name),
                RuntimeMember("MapGetI32Raw", $"{SandboxValueName},{SandboxValueName}", Int32Name),
                RuntimeMember("ConcatString", $"{SandboxContextName},{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("AbsI32", SandboxValueName, SandboxValueName),
                RuntimeMember("MinI32", $"{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("MaxI32", $"{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("ClampI32", $"{SandboxValueName},{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("SqrtF64", SandboxValueName, SandboxValueName),
                RuntimeMember("SqrtF64Raw", DoubleName, DoubleName),
                RuntimeMember("FloorF64", SandboxValueName, SandboxValueName),
                RuntimeMember("FloorF64Raw", DoubleName, DoubleName),
                RuntimeMember("CeilF64", SandboxValueName, SandboxValueName),
                RuntimeMember("CeilF64Raw", DoubleName, DoubleName),
                RuntimeMember("RoundF64", SandboxValueName, SandboxValueName),
                RuntimeMember("RoundF64Raw", DoubleName, DoubleName),
                RuntimeMember("CreateValueArray", $"{SandboxContextName},{Int32Name}", SandboxValueArrayName),
                RuntimeMember("ChargeValueArray", $"{SandboxContextName},{Int32Name}", VoidName),
                RuntimeMember("CreateLiteralValueArray", Int32Name, SandboxValueArrayName),
                RuntimeMember("ListEmpty", $"{SandboxContextName},{SandboxTypeName}", SandboxValueName),
                RuntimeMember("ListOf", $"{SandboxContextName},{SandboxValueArrayName}", SandboxValueName),
                RuntimeMember("ListLiteral", $"{SandboxContextName},{SandboxTypeName},{SandboxValueArrayName}", SandboxValueName),
                RuntimeMember("ListLiteralValue", $"{SandboxTypeName},{SandboxValueArrayName}", SandboxValueName),
                RuntimeMember("ListCount", $"{SandboxContextName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("ListGet", $"{SandboxContextName},{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("ListAdd", $"{SandboxContextName},{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("MapEmpty", $"{SandboxContextName},{SandboxTypeName},{SandboxTypeName}", SandboxValueName),
                RuntimeMember("MapLiteral", $"{SandboxContextName},{SandboxTypeName},{SandboxTypeName},{SandboxValueArrayName},{SandboxValueArrayName}", SandboxValueName),
                RuntimeMember("MapLiteralValue", $"{SandboxTypeName},{SandboxTypeName},{SandboxValueArrayName},{SandboxValueArrayName}", SandboxValueName),
                RuntimeMember("MapContainsKey", $"{SandboxContextName},{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("MapGet", $"{SandboxContextName},{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("MapSet", $"{SandboxContextName},{SandboxValueName},{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("MapRemove", $"{SandboxContextName},{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("RecordNew", $"{SandboxContextName},{SandboxValueArrayName}", SandboxValueName),
                RuntimeMember("RecordGet", $"{SandboxContextName},{SandboxValueName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("CallBinding", $"{SandboxContextName},{StringName},{SandboxValueArrayName}", SandboxValueName),
                RuntimeMember("CallBinding1", $"{SandboxContextName},{StringName},{SandboxValueName}", SandboxValueName),
                RuntimeMember("CallBinding2", $"{SandboxContextName},{StringName},{SandboxValueName},{SandboxValueName}", SandboxValueName)
            },
            new HashSet<string>(StringComparer.Ordinal) {
                "System.IO.", "System.Net.", "System.Reflection.", "System.Runtime.Loader.",
                "System.Runtime.InteropServices.", "System.Diagnostics.", "System.Threading.",
                "System.Threading.Tasks.", "System.Activator", "System.Environment",
                "System.GC", "System.Delegate", "System.IServiceProvider",
                "System.Linq.Expressions.", "Microsoft.CSharp."
            },
            RuntimeFacadeIdentityDefaults(),
            "dotboxd-verifier-9");

    public bool IsMemberAllowed(string memberSignature)
    {
        ArgumentNullException.ThrowIfNull(memberSignature);
        return AllowedMembers.Contains(memberSignature);
    }

    public VerificationPolicy WithExpectedManifest(VerificationManifestIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return this with { ExpectedManifestIdentity = identity };
    }

    public string AllowlistHash
        => _allowlistHash ??= Hash(
            "allowlist",
            AllowedAssemblies
                .Concat(AllowedAssemblyIdentities)
                .Concat(AllowedTypes)
                .Concat(AllowedMembers)
                .Concat(ForbiddenTypePrefixes));

    public string RuntimeFacadeHash
        => _runtimeFacadeHash ??= Hash(
            "runtime-facade",
            AllowedMembers
                .Where(m => m.StartsWith($"{CompiledRuntimeName}.", StringComparison.Ordinal))
                .Concat(RuntimeFacadeIdentities));

    private void InvalidateHashes()
    {
        _allowlistHash = null;
        _runtimeFacadeHash = null;
    }

    private static string Hash(string prefix, IEnumerable<string> values)
    {
        var builder = new StringBuilder();
        AppendHashPart(builder, prefix);

        foreach (var value in values.Order(StringComparer.Ordinal))
        {
            AppendHashPart(builder, value);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    private static void AppendHashPart(StringBuilder builder, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        builder.Append(value.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(':').Append(value);
    }

    private static IReadOnlySet<string> Freeze(IEnumerable<string> values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values, parameterName);
        if (values.Any(static value => value is null))
        {
            throw new ArgumentException("Values cannot contain null entries.", parameterName);
        }

        return values.ToFrozenSet(StringComparer.Ordinal);
    }

    private static string RequireVersion(string? value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        return value;
    }

}
