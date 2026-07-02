using System.Reflection;
using System.Reflection.Emit;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests.Verifier.Generated;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Kernels.Tests.Verifier.Core;

public sealed class VerifierDocumentedAttackMatrixTests
{
    public static TheoryData<string, Func<byte[]>, string[]> DocumentedAttackCases()
        => new() {
            { "exception handlers", ExceptionHandlerAssembly, ["V-EXCEPTION"] },
            { "embedded resources", EmbeddedResourceAssembly, ["V-RESOURCE"] },
            { "Thread.Start", ThreadStartAssembly, ["V-TYPE-FORBIDDEN", "V-MEMBER"] },
            { "raw Stream", StreamAssembly, ["V-TYPE-FORBIDDEN", "V-MEMBER"] },
            { "IServiceProvider.GetService", ServiceProviderAssembly, ["V-TYPE-FORBIDDEN", "V-MEMBER"] },
            { "unmanaged function pointer signature", FunctionPointerSignatureAssembly, ["V-FUNCTION-SIGNATURE"] }
        };

    [Theory]
    [MemberData(nameof(DocumentedAttackCases))]
    public async Task Verifier_rejects_documented_boundary_attacks(
        string name,
        Func<byte[]> build,
        string[] expectedCodes)
    {
        var result = await VerifierTestHelpers.VerifyAsync(build());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => expectedCodes.Contains(d.Code));
        Assert.NotEmpty(name);
    }

    [Fact]
    public async Task Verifier_rejects_uncharged_string_literal_helper()
    {
        var result = await VerifierTestHelpers.VerifyAsync(UnchargedStringLiteralHelperAssembly());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d =>
            (d.Code == "V-COMPILED-SHAPE" || d.Code == "V-MEMBER") &&
            d.Message.Contains(nameof(CompiledRuntime.StringLiteralValue), StringComparison.Ordinal));
    }

    private static byte[] ExceptionHandlerAssembly()
        => VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var method = DefineExecute(type);
            var local = method.GetILGenerator().DeclareLocal(typeof(SandboxValue));
            var il = method.GetILGenerator();
            var end = il.BeginExceptionBlock();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stloc, local);
            il.Emit(OpCodes.Leave_S, end);
            il.BeginCatchBlock(typeof(Exception));
            il.Emit(OpCodes.Pop);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stloc, local);
            il.Emit(OpCodes.Leave_S, end);
            il.EndExceptionBlock();
            il.Emit(OpCodes.Ldloc, local);
            il.Emit(OpCodes.Ret);
        });

    private static byte[] EmbeddedResourceAssembly()
    {
        const string source = """
            namespace DotBoxD.Kernels.Generated;

            public static class Module_0123456789abcdef
            {
                public static DotBoxD.Kernels.Sandbox.SandboxValue Execute(
                    DotBoxD.Kernels.Sandbox.SandboxContext context,
                    DotBoxD.Kernels.Sandbox.SandboxValue input) => input;
            }
            """;
        using var output = new MemoryStream();
        var compilation = CSharpCompilation.Create(
            "ResourceAttack",
            [CSharpSyntaxTree.ParseText(source)],
            TrustedPlatformReferences().Append(MetadataReference.CreateFromFile(typeof(SandboxValue).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var resource = new ResourceDescription(
            "payload.bin",
            () => new MemoryStream([1, 2, 3], writable: false),
            isPublic: true);
        var result = compilation.Emit(output, manifestResources: [resource]);
        if (!result.Success)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics));
        }

        return output.ToArray();
    }

    private static byte[] ThreadStartAssembly()
        => VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var method = DefineExecute(type);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Callvirt, typeof(Thread).GetMethod(nameof(Thread.Start), Type.EmptyTypes)!);
            ReturnInput(il);
        });

    private static byte[] StreamAssembly()
        => VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var method = DefineExecute(type);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Call, typeof(Stream).GetMethod(nameof(Stream.Synchronized), [typeof(Stream)])!);
            il.Emit(OpCodes.Pop);
            ReturnInput(il);
        });

    private static byte[] ServiceProviderAssembly()
        => VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var method = DefineExecute(type);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Callvirt, typeof(IServiceProvider).GetMethod(nameof(IServiceProvider.GetService))!);
            il.Emit(OpCodes.Pop);
            ReturnInput(il);
        });

    private static byte[] FunctionPointerSignatureAssembly()
    {
        const string source = """
            using System.Runtime.CompilerServices;

            namespace DotBoxD.Kernels.Generated;

            public static unsafe class Module_0123456789abcdef
            {
                public static DotBoxD.Kernels.Sandbox.SandboxValue Execute(
                    DotBoxD.Kernels.Sandbox.SandboxContext context,
                    DotBoxD.Kernels.Sandbox.SandboxValue input) => input;

                private static DotBoxD.Kernels.Sandbox.SandboxValue Fn_0(
                    DotBoxD.Kernels.Sandbox.SandboxContext context,
                    delegate* unmanaged[Cdecl]<void> callback) => null!;
            }
            """;
        using var output = new MemoryStream();
        var compilation = CSharpCompilation.Create(
            "FunctionPointerAttack",
            [CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview))],
            TrustedPlatformReferences().Append(MetadataReference.CreateFromFile(typeof(SandboxValue).Assembly.Location)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        var result = compilation.Emit(output);
        if (!result.Success)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics));
        }

        return output.ToArray();
    }

    private static byte[] UnchargedStringLiteralHelperAssembly()
        => VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var fn = type.DefineMethod(
                "Fn_0",
                MethodAttributes.Private | MethodAttributes.Static,
                typeof(SandboxValue),
                [typeof(SandboxContext)]);
            var fnIl = fn.GetILGenerator();
            var value = fnIl.DeclareLocal(typeof(SandboxValue));
            EmitEnterCall(fnIl);
            EmitChargeFuel(fnIl);
            fnIl.Emit(OpCodes.Ldstr, "hello");
            fnIl.Emit(
                OpCodes.Call,
                typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.StringLiteralValue), [typeof(string)])!);
            fnIl.Emit(OpCodes.Stloc, value);
            EmitExitCall(fnIl);
            fnIl.Emit(OpCodes.Ldloc, value);
            fnIl.Emit(OpCodes.Ret);

            var il = DefineExecute(type).GetILGenerator();
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.ValidateEntrypointInput))!);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, fn);
            il.Emit(OpCodes.Ret);
        });

    private static IEnumerable<MetadataReference> TrustedPlatformReferences()
    {
        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "";
        foreach (var path in trustedPlatformAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            yield return MetadataReference.CreateFromFile(path);
        }
    }

    private static MethodBuilder DefineExecute(TypeBuilder type)
        => type.DefineMethod(
            "Execute",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(SandboxValue),
            [typeof(SandboxContext), typeof(SandboxValue)]);

    private static void ReturnInput(ILGenerator il)
    {
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitEnterCall(ILGenerator il)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.EnterCall))!);
    }

    private static void EmitChargeFuel(ILGenerator il)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.ChargeFuel))!);
    }

    private static void EmitExitCall(ILGenerator il)
    {
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.ExitCall))!);
    }
}
