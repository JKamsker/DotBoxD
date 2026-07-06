using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Tests.Verifier.Generated;

namespace DotBoxD.Kernels.Tests.Verifier.Core.Signatures;

public sealed class VerifierCustomModifierSignatureTests
{
    private const byte ElementTypePointer = 0x0f;
    private const byte ElementTypePinned = 0x45;

    [Fact]
    public async Task Verifier_rejects_pinned_generated_local_signatures()
    {
        var result = await VerifierTestHelpers.VerifyAsync(AssemblyWithPinnedSandboxValueLocal());

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d =>
            d.Code == "V-FUNCTION-SIGNATURE" &&
            d.Message.Contains("pinned", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Execute SandboxContext parameter", "V-EXECUTE-SIGNATURE")]
    [InlineData("Fn_1 SandboxValue return", "V-FUNCTION-SIGNATURE")]
    public async Task Verifier_rejects_custom_modified_generated_method_signatures(
        string shape,
        string expectedCode)
    {
        var assembly = shape.StartsWith("Execute", StringComparison.Ordinal)
            ? AssemblyWithModifiedExecuteParameter()
            : AssemblyWithModifiedFunctionReturn();

        var result = await VerifierTestHelpers.VerifyAsync(assembly);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Code == expectedCode);
    }

    private static byte[] AssemblyWithModifiedExecuteParameter()
        => VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var function = DefineValidFunction(type, "Fn_0");
            var execute = type.DefineMethod(
                "Execute",
                MethodAttributes.Public | MethodAttributes.Static,
                CallingConventions.Standard,
                typeof(SandboxValue),
                returnTypeRequiredCustomModifiers: null,
                returnTypeOptionalCustomModifiers: null,
                parameterTypes: [typeof(SandboxContext), typeof(SandboxValue)],
                parameterTypeRequiredCustomModifiers: [[typeof(SandboxValue)], Type.EmptyTypes],
                parameterTypeOptionalCustomModifiers: null);

            EmitExecuteBody(execute, function);
        });

    private static byte[] AssemblyWithModifiedFunctionReturn()
        => VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var function = type.DefineMethod(
                "Fn_1",
                MethodAttributes.Private | MethodAttributes.Static,
                CallingConventions.Standard,
                typeof(SandboxValue),
                returnTypeRequiredCustomModifiers: [typeof(SandboxValue)],
                returnTypeOptionalCustomModifiers: null,
                parameterTypes: [typeof(SandboxContext)],
                parameterTypeRequiredCustomModifiers: null,
                parameterTypeOptionalCustomModifiers: null);
            EmitFunctionBody(function);

            var execute = type.DefineMethod(
                "Execute",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(SandboxValue),
                [typeof(SandboxContext), typeof(SandboxValue)]);
            EmitExecuteBody(execute, function);
        });

    private static byte[] AssemblyWithPinnedSandboxValueLocal()
    {
        var assembly = VerifierTestHelpers.BuildGeneratedAssembly(type =>
        {
            var function = type.DefineMethod(
                "Fn_0",
                MethodAttributes.Private | MethodAttributes.Static,
                typeof(SandboxValue),
                [typeof(SandboxContext)]);
            EmitFunctionBody(function, declarePatchablePointerLocal: true);

            var execute = type.DefineMethod(
                "Execute",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(SandboxValue),
                [typeof(SandboxContext), typeof(SandboxValue)]);
            EmitExecuteBody(execute, function);
        });

        PatchFirstFunctionLocalPointerToPinned(assembly);
        return assembly;
    }

    private static MethodBuilder DefineValidFunction(TypeBuilder type, string name)
    {
        var function = type.DefineMethod(
            name,
            MethodAttributes.Private | MethodAttributes.Static,
            typeof(SandboxValue),
            [typeof(SandboxContext)]);
        EmitFunctionBody(function);
        return function;
    }

    private static void EmitExecuteBody(MethodBuilder execute, MethodInfo function)
    {
        var il = execute.GetILGenerator();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.ValidateEntrypointInput))!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, function);
        il.Emit(OpCodes.Ret);
    }

    private static void EmitFunctionBody(MethodBuilder function, bool declarePatchablePointerLocal = false)
    {
        var il = function.GetILGenerator();
        if (declarePatchablePointerLocal)
        {
            il.DeclareLocal(typeof(SandboxValue).MakePointerType());
        }

        var value = il.DeclareLocal(typeof(SandboxValue));
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.EnterCall))!);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.ChargeFuel))!);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.I32))!);
        il.Emit(OpCodes.Stloc, value);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.ExitCall))!);
        il.Emit(OpCodes.Ldloc, value);
        il.Emit(OpCodes.Ret);
    }

    private static void PatchFirstFunctionLocalPointerToPinned(byte[] assembly)
    {
        using var peReader = new PEReader(new MemoryStream(assembly, writable: false));
        var reader = peReader.GetMetadataReader();
        var localSignature = FindFirstFunctionLocalSignature(peReader, reader);
        if (localSignature.Length < 3 ||
            localSignature[0] != 0x07 ||
            localSignature[2] != ElementTypePointer)
        {
            throw new InvalidOperationException("Expected a patchable pointer local signature.");
        }

        var offset = FindUniqueSequence(assembly, localSignature);
        assembly[offset + 2] = ElementTypePinned;
    }

    private static byte[] FindFirstFunctionLocalSignature(PEReader peReader, MetadataReader reader)
    {
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(typeHandle);
            foreach (var methodHandle in type.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (reader.GetString(method.Name) != "Fn_0")
                {
                    continue;
                }

                var body = peReader.GetMethodBody(method.RelativeVirtualAddress);
                var signature = reader.GetStandaloneSignature(body.LocalSignature);
                return reader.GetBlobBytes(signature.Signature);
            }
        }

        throw new InvalidOperationException("Expected generated Fn_0 method.");
    }

    private static int FindUniqueSequence(byte[] bytes, byte[] sequence)
    {
        var found = -1;
        for (var i = 0; i <= bytes.Length - sequence.Length; i++)
        {
            if (!SequenceEqual(bytes, sequence, i))
            {
                continue;
            }

            if (found >= 0)
            {
                throw new InvalidOperationException("Expected local signature to appear once.");
            }

            found = i;
        }

        return found >= 0
            ? found
            : throw new InvalidOperationException("Expected local signature in assembly bytes.");
    }

    private static bool SequenceEqual(byte[] bytes, byte[] sequence, int offset)
    {
        for (var i = 0; i < sequence.Length; i++)
        {
            if (bytes[offset + i] != sequence[i])
            {
                return false;
            }
        }

        return true;
    }
}
