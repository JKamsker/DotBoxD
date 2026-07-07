using DotBoxD.Services.Attributes;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DotBoxD.Services.SourceGenerator.Tests.Generation;

internal static class CodegenRegressionTestSupport
{
    internal static (Compilation Final, GeneratorDriverRunResult RunResult) Run(string source)
    {
        var compilation = GeneratorTestHelper.CreateCompilation(source);
        var driver = GeneratorTestHelper.CreateDriver().RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var finalCompilation = ((CSharpCompilation)compilation).AddSyntaxTrees(runResult.GeneratedTrees);
        return (finalCompilation, runResult);
    }

    internal static void AssertCompiles(Compilation final)
    {
        using var ms = new MemoryStream();
        var emit = final.Emit(ms);
        if (!emit.Success)
        {
            var errs = string.Join(
                Environment.NewLine,
                emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString()));
            var dump = string.Join(
                Environment.NewLine + "----" + Environment.NewLine,
                final.SyntaxTrees.Select(t => t.FilePath + Environment.NewLine + t.GetText()));
            throw new InvalidOperationException("Emit failed:" + Environment.NewLine + errs + Environment.NewLine + dump);
        }

        emit.Success.Should().BeTrue();
    }

    internal static (Compilation Final, GeneratorDriverRunResult RunResult) RunWithPreviewByRefLikeGenerics(string source)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            assemblyName: $"GenPreviewTest_{Guid.NewGuid():N}",
            syntaxTrees:
            [
                CSharpSyntaxTree.ParseText(
                    "[assembly: System.Runtime.Versioning.TargetFramework(\".NETCoreApp,Version=v10.0\", FrameworkDisplayName = \".NET 10.0\")]",
                    parseOptions),
                CSharpSyntaxTree.ParseText(source, parseOptions),
            ],
            references: Net10ReferenceAssemblies()
                .Append(MetadataReference.CreateFromFile(typeof(RpcServiceAttribute).Assembly.Location)),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var driver = GeneratorTestHelper.CreateDriver(parseOptions).RunGenerators(compilation);
        var runResult = driver.GetRunResult();
        var finalCompilation = compilation.AddSyntaxTrees(runResult.GeneratedTrees);
        return (finalCompilation, runResult);
    }

    private static IEnumerable<MetadataReference> Net10ReferenceAssemblies()
    {
        var dotnetRoot = FindDotnetRoot();
        var packsRoot = Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref");
        var referenceDirectory = Directory.EnumerateDirectories(packsRoot)
            .Select(static directory => new
            {
                Directory = Path.Combine(directory, "ref", "net10.0"),
                Version = Version.TryParse(Path.GetFileName(directory), out var version) ? version : null,
            })
            .Where(static candidate => candidate.Version is { Major: 10 } &&
                Directory.Exists(candidate.Directory))
            .OrderByDescending(static candidate => candidate.Version)
            .Select(static candidate => candidate.Directory)
            .FirstOrDefault();

        if (referenceDirectory is null)
        {
            throw new DirectoryNotFoundException(
                $"Could not find the .NET 10 reference pack under '{packsRoot}'.");
        }

        return Directory.EnumerateFiles(referenceDirectory, "*.dll")
            .Select(static reference => MetadataReference.CreateFromFile(reference));
    }

    private static string FindDotnetRoot()
    {
        var root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(root) && HasNet10ReferencePack(root))
        {
            return root;
        }

        var runtimeDirectory = Directory.GetParent(typeof(object).Assembly.Location);
        var appDirectory = runtimeDirectory?.Parent;
        var sharedDirectory = appDirectory?.Parent;
        var dotnetDirectory = sharedDirectory?.Parent;
        if (dotnetDirectory is not null && HasNet10ReferencePack(dotnetDirectory.FullName))
        {
            return dotnetDirectory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the dotnet installation root.");
    }

    private static bool HasNet10ReferencePack(string dotnetRoot)
        => Directory.Exists(Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref")) &&
           Directory.EnumerateDirectories(Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref"))
               .Any(static directory =>
                   Version.TryParse(Path.GetFileName(directory), out var version) &&
                   version.Major == 10 &&
                   Directory.Exists(Path.Combine(directory, "ref", "net10.0")));

    internal sealed class OverloadProbeClient : global::DotBoxD.Services.Server.IRpcInvoker
    {
        public int WithRequestWithResponseOverloadCalls;
        public int WithResponseOverloadCalls;
        public int WithRequestNoResponseOverloadCalls;
        public int NoRequestNoResponseOverloadCalls;

        public bool IsConnected => true;

        public Task ConnectAsync(System.Threading.CancellationToken ct = default) => Task.CompletedTask;

        public Task<TR> InvokeAsync<TQ, TR>(
            string s,
            string m,
            TQ q,
            System.Threading.CancellationToken ct = default)
        {
            WithRequestWithResponseOverloadCalls++;
            return Task.FromResult(default(TR)!);
        }

        public Task<TR> InvokeAsync<TR>(
            string s,
            string m,
            System.Threading.CancellationToken ct = default)
        {
            WithResponseOverloadCalls++;
            return Task.FromResult(default(TR)!);
        }

        public Task InvokeAsync<TQ>(
            string s,
            string m,
            TQ q,
            System.Threading.CancellationToken ct = default)
        {
            WithRequestNoResponseOverloadCalls++;
            return Task.CompletedTask;
        }

        public Task InvokeAsync(
            string s,
            string m,
            System.Threading.CancellationToken ct = default)
        {
            NoRequestNoResponseOverloadCalls++;
            return Task.CompletedTask;
        }

        public System.Threading.Tasks.ValueTask DisposeAsync() => default;

        public Task<TR> InvokeOnInstanceAsync<TQ, TR>(
            string s,
            string id,
            string m,
            TQ q,
            System.Threading.CancellationToken ct = default)
            => InvokeAsync<TQ, TR>(s, m, q, ct);

        public Task<TR> InvokeOnInstanceAsync<TR>(
            string s,
            string id,
            string m,
            System.Threading.CancellationToken ct = default)
            => InvokeAsync<TR>(s, m, ct);

        public Task InvokeOnInstanceAsync<TQ>(
            string s,
            string id,
            string m,
            TQ q,
            System.Threading.CancellationToken ct = default)
            => InvokeAsync<TQ>(s, m, q, ct);

        public Task InvokeOnInstanceAsync(
            string s,
            string id,
            string m,
            System.Threading.CancellationToken ct = default)
            => InvokeAsync(s, m, ct);
    }
}

/// <summary>A minimal IRpcInvoker that does nothing for generated proxy construction and stub tests.</summary>
internal sealed class NullClient : global::DotBoxD.Services.Server.IRpcInvoker
{
    public bool IsConnected => true;

    public Task ConnectAsync(System.Threading.CancellationToken ct = default) => Task.CompletedTask;

    public Task<TR> InvokeAsync<TQ, TR>(
        string service,
        string method,
        TQ request,
        System.Threading.CancellationToken ct = default) =>
        Task.FromResult(default(TR)!);

    public Task<TR> InvokeAsync<TR>(
        string service,
        string method,
        System.Threading.CancellationToken ct = default) =>
        Task.FromResult(default(TR)!);

    public Task InvokeAsync<TQ>(
        string service,
        string method,
        TQ request,
        System.Threading.CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task InvokeAsync(
        string service,
        string method,
        System.Threading.CancellationToken ct = default) =>
        Task.CompletedTask;

    public System.Threading.Tasks.ValueTask DisposeAsync() => default;

    public Task<TR> InvokeOnInstanceAsync<TQ, TR>(
        string service,
        string instanceId,
        string method,
        TQ request,
        System.Threading.CancellationToken ct = default) =>
        InvokeAsync<TQ, TR>(service, method, request, ct);

    public Task<TR> InvokeOnInstanceAsync<TR>(
        string service,
        string instanceId,
        string method,
        System.Threading.CancellationToken ct = default) =>
        InvokeAsync<TR>(service, method, ct);

    public Task InvokeOnInstanceAsync<TQ>(
        string service,
        string instanceId,
        string method,
        TQ request,
        System.Threading.CancellationToken ct = default) =>
        InvokeAsync(service, method, request, ct);

    public Task InvokeOnInstanceAsync(
        string service,
        string instanceId,
        string method,
        System.Threading.CancellationToken ct = default) =>
        InvokeAsync(service, method, ct);
}
