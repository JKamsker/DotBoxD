using System.Runtime.Loader;
using DotBoxD.Plugins.Fody;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Weaving;

public sealed class InvokeAsyncClosureWeaverIlTests
{
    [Fact]
    public void Weaver_emits_runnable_il_for_short_branches_generic_closures_and_target_framework_corlib()
    {
        using var fixture = new MemoryStream(CompileFixture());
        using var module = ModuleDefinition.ReadModule(fixture);
        var info = new List<string>();
        var warnings = new List<string>();
        // Precondition: the capture reads sit behind a short branch that rewriting would overflow.
        Assert.Contains(
            StateMachineMoveNext(module, "InvokeAsync_Plain").Body.Instructions,
            static instruction => instruction.OpCode.Code is Code.Brfalse_S or Code.Brtrue_S);

        var (interceptors, accesses) = RunWeaver(module, info, warnings);

        Assert.Empty(warnings);
        Assert.Equal(1, interceptors);
        Assert.Equal(ReadCount, accesses);
        Assert.Contains(info, message =>
            message.Contains("InvokeAsync_Generic", StringComparison.Ordinal) &&
            message.Contains("closure type is generic", StringComparison.Ordinal));

        var plainMoveNext = StateMachineMoveNext(module, "InvokeAsync_Plain");
        Assert.DoesNotContain(plainMoveNext.Body.Instructions, IsCaptureHelperCall);
        var targetGetters = plainMoveNext.Body.Instructions
            .Select(static instruction => instruction.Operand)
            .OfType<MethodReference>()
            .Where(static method => method.Name == "get_Target")
            .ToArray();
        Assert.Equal(ReadCount, targetGetters.Length);
        Assert.All(targetGetters, static getter => Assert.Equal("mscorlib", getter.DeclaringType.Scope.Name));
        Assert.Contains(
            StateMachineMoveNext(module, "InvokeAsync_Generic").Body.Instructions,
            IsCaptureHelperCall);

        using var woven = new MemoryStream();
        module.Write(woven);
        var context = new AssemblyLoadContext("InvokeAsyncWeaver_" + Guid.NewGuid().ToString("N"), isCollectible: true);
        try
        {
            woven.Position = 0;
            var caller = context.LoadFromStream(woven).GetType("Sample.Caller", throwOnError: true)!;
            Assert.Equal(ReadCount * 7, caller.GetMethod("Plain")!.Invoke(null, [7, true]));
            Assert.Equal(0, caller.GetMethod("Plain")!.Invoke(null, [7, false]));
            Assert.Equal(3, caller.GetMethod("Generic")!.MakeGenericMethod(typeof(string)).Invoke(null, ["x", 3]));
        }
        finally
        {
            context.Unload();
        }
    }

    private const int ReadCount = 6;

    private static byte[] CompileFixture()
    {
        var reads = string.Concat(Enumerable.Repeat(
            "                    total += __ReadCapture<int>(lambda, \"value\");\n",
            ReadCount));
        var source = $$"""
            using System;
            using System.Threading.Tasks;

            namespace DotBoxD.Plugins.Generated
            {
                public static class InvokeAsyncInterceptors
                {
                    public static async Task<int> InvokeAsync_Plain(Func<int> lambda, bool flag)
                    {
                        await Task.FromResult(0);
                        var total = 0;
                        if (flag)
                        {
            {{reads}}
                        }

                        return total;
                    }

                    public static async Task<int> InvokeAsync_Generic(Func<int> lambda)
                    {
                        await Task.FromResult(0);
                        return __ReadCapture<int>(lambda, "value");
                    }

                    private static T __ReadCapture<T>(Delegate lambda, string fieldName)
                    {
                        var target = lambda.Target;
                        return (T)target.GetType().GetField(fieldName).GetValue(target);
                    }
                }
            }

            namespace Sample
            {
                using DotBoxD.Plugins.Generated;

                public static class Caller
                {
                    public static int Plain(int value, bool flag)
                        => InvokeAsyncInterceptors.InvokeAsync_Plain(() => value, flag).GetAwaiter().GetResult();

                    public static int Generic<T>(T item, int value)
                        => InvokeAsyncInterceptors.InvokeAsync_Generic(() => item == null ? 0 : value).GetAwaiter().GetResult();
                }
            }
            """;
        var compilation = CSharpCompilation.Create(
            "InvokeAsyncWeaverFixture_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(source)],
            [MetadataReference.CreateFromFile(PackageReference(
                "microsoft.netframework.referenceassemblies.net461",
                "build/.NETFramework/v4.6.1/mscorlib.dll"))],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        return stream.ToArray();
    }

    // The weaver compiles against FodyHelpers' re-signed Mono.Cecil while this project references the
    // Mono.Cecil package, so the call is bound at runtime where both resolve to the same assembly.
    private static (int Interceptors, int Accesses) RunWeaver(
        ModuleDefinition module,
        List<string> info,
        List<string> warnings)
    {
        var weaver = typeof(DotBoxDInvokeAsyncWeaverNames).Assembly
            .GetType("DotBoxD.Plugins.Fody.InvokeAsyncClosureCaptureWeaver", throwOnError: true)!;
        var result = weaver.GetMethod("Rewrite")!.Invoke(
            null,
            [module, new Action<string>(info.Add), new Action<string>(warnings.Add)])!;
        return (
            (int)result.GetType().GetProperty("RewrittenInterceptors")!.GetValue(result)!,
            (int)result.GetType().GetProperty("RewrittenAccesses")!.GetValue(result)!);
    }

    private static MethodDefinition StateMachineMoveNext(ModuleDefinition module, string interceptorName)
        => module.GetType(DotBoxDInvokeAsyncWeaverNames.GeneratedInterceptorsFullName)
            .NestedTypes
            .Single(type => type.Name.StartsWith("<" + interceptorName + ">", StringComparison.Ordinal))
            .Methods
            .Single(static method => method.Name == DotBoxDInvokeAsyncWeaverNames.MoveNextMethodName);

    private static bool IsCaptureHelperCall(Instruction instruction)
        => instruction.Operand is MethodReference { Name: DotBoxDInvokeAsyncWeaverNames.ReadCaptureMethodName };

    private static string PackageReference(string packageId, string relativePath)
    {
        var packageRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (string.IsNullOrWhiteSpace(packageRoot))
        {
            packageRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget",
                "packages");
        }

        var packageDirectory = Path.Combine(packageRoot, packageId);
        var version = Directory.EnumerateDirectories(packageDirectory)
            .OrderByDescending(static directory => directory, StringComparer.Ordinal)
            .First();
        return Path.Combine(version, relativePath);
    }
}
