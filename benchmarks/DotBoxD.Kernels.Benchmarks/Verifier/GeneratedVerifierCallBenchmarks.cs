using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Verifier.Generated;

namespace DotBoxD.Kernels.Benchmarks.Verifier;

using System.Reflection;
using System.Reflection.Emit;
using BenchmarkDotNet.Attributes;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Verifier;

[MemoryDiagnoser]
public class GeneratedVerifierCallBenchmarks
{
    private byte[] _assemblyBytes = [];
    private ArtifactManifest _manifest = null!;
    private readonly VerificationPolicy _policy = VerificationPolicy.BoxedValueDefaults();
    private readonly GeneratedAssemblyVerifier _verifier = new();

    [Params(100, 1_000, 10_000)]
    public int CallCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _assemblyBytes = BuildAssembly(CallCount);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(_assemblyBytes)).ToLowerInvariant();
        _manifest = new ArtifactManifest(
            1,
            "benchmark",
            "module",
            "plan",
            "policy",
            "bindings",
            _policy.RuntimeFacadeHash,
            "compiler",
            "type-system",
            "effect-analysis",
            _policy.VerifierVersion,
            "1.0.0",
            "net10.0",
            [],
            hash,
            DateTimeOffset.UtcNow);
    }

    [Benchmark]
    public async ValueTask<VerificationResult> VerifyRepeatedRuntimeCalls()
        => await _verifier.VerifyAsync(_assemblyBytes, _manifest, _policy, CancellationToken.None);

    private static byte[] BuildAssembly(int callCount)
    {
        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName("Generated" + Guid.NewGuid().ToString("N")),
            typeof(object).Assembly);
        var module = assembly.DefineDynamicModule("GeneratedModule");
        var type = module.DefineType(
            "DotBoxD.Kernels.Generated.Module_0123456789abcdef",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var method = type.DefineMethod(
            "Execute",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(SandboxValue),
            [typeof(SandboxContext), typeof(SandboxValue)]);
        var il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.ValidateEntrypointInput)));
        for (var i = 0; i < callCount; i++)
        {
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.ChargeFuel)));
        }

        il.Emit(OpCodes.Call, Runtime(nameof(Kernels.Runtime.CompiledRuntime.Unit)));
        il.Emit(OpCodes.Ret);
        type.CreateType();

        using var stream = new MemoryStream();
        assembly.Save(stream);
        return stream.ToArray();
    }

    private static MethodInfo Runtime(string name)
        => typeof(Kernels.Runtime.CompiledRuntime).GetMethod(name)!;
}
