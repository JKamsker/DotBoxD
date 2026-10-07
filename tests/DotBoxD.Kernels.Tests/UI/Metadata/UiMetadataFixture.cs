using System.Runtime.Loader;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Tests.UI.Authoring;
using DotBoxD.UI.Runtime;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Kernels.Tests.UI.Metadata;

internal static class UiMetadataFixture
{
    internal const string Getter = "public struct Box { private int stored; public int Value { get => stored + 1; set => stored = value; } }";
    internal const string Setter = "public struct Box { private int stored; public int Value { get => stored; set => stored = value + 1; } }";
    internal const string Fields = "public struct Box { public int Value; }";
    internal const string Overlay = "[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)] public struct Box { [System.Runtime.InteropServices.FieldOffset(0)] public int First; [System.Runtime.InteropServices.FieldOffset(0)] public int Second; }";

    private static string Library(string type) => "namespace ExternalDtos { " + type + " }";

    private static string Handler(string body, string type) => """
        using DotBoxD.Abstractions;
        using DotBoxD.UI;
        using DotBoxD.UI.Authoring;
        using ExternalDtos;
        public sealed class Container { public Box Box { get; set; } }
        public sealed class GenericContainer<T> { public T Item { get; set; } }
        public static partial class Counter
        {
            [UiLocalHandler] public static int Handle(int value)
        """ + body + "[KernelMethod] public static Box Make(int value) => new Box { " +
        (type == Overlay ? "First = value, Second = value + 1" : "Value = value") + " }; }";

    private static byte[] Image(string type)
    {
        using var stream = new MemoryStream();
        var emitted = UiGeneratorFixture.Generate(Library(type)).Compilation.Emit(stream);
        Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));
        return stream.ToArray();
    }

    public static void AssertUnsupported(string type, string body, bool metadata = true)
    {
        var result = metadata
            ? UiGeneratorFixture.Generate(Handler(body, type), MetadataReference.CreateFromImage(Image(type)))
            : UiGeneratorFixture.Generate(Handler(body, type) + Library(type));
        Assert.Empty(result.Compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Single(result.Diagnostics.Where(d => d.Id == "DBXU001"));
        Assert.Empty(result.Result.GeneratedTrees);
    }

    public static async Task AssertMatchesNative(string type, string body, ExecutionMode mode, bool metadata = true)
    {
        var bytes = Image(type);
        using var stream = new MemoryStream(bytes);
        _ = AssemblyLoadContext.Default.LoadFromStream(stream);
        var reference = MetadataReference.CreateFromImage(bytes);
        var generated = metadata
            ? UiGeneratorFixture.Generate(Handler(body, type), reference)
            : UiGeneratorFixture.Generate(Handler(body, type) + Library(type));
        Assert.Empty(generated.Compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
        var expected = (int)UiGeneratorFixture.Emit(generated.Compilation).GetType("Counter")!.GetMethod("Handle")!.Invoke(null, [42])!;
        var packageSource = Handler(body, type) + """
            public static partial class Counter
            {
                public static UiPackage Package()
                {
                    var b = new UiBuilder();
                    var input = b.State(42);
                    var output = b.State(0);
                    return b.Build(b.Button("Run", b.Kernel(HandleUiKernel(), input), output));
                }
            }
            """;
        var result = metadata ? UiGeneratorFixture.Generate(packageSource, reference)
            : UiGeneratorFixture.Generate(packageSource + Library(type));
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        var package = (DotBoxD.UI.UiPackage)UiGeneratorFixture.Emit(result.Output).GetType("Counter")!.GetMethod("Package")!.Invoke(null, null)!;
        using var sandbox = UiTestFixture.CompiledSandbox();
        await using var session = await new UiHost(sandbox, SandboxPolicyBuilder.Create().Build(),
            execution: new SandboxExecutionOptions { Mode = mode, AllowFallbackToInterpreter = false })
            .InstallAsync(package, new RecordingUiRenderer());
        Assert.Equal(expected, (await session.DispatchAsync(1)).State.Single(s => s.SlotId == 2).Value.Integer);
    }
}
