using System.Runtime.Loader;
using DotBoxD.Services.Generated;
using Microsoft.CodeAnalysis;

namespace DotBoxD.Services.SourceGenerator.Tests.Collisions;

public sealed class GeneratedAssemblyCollisionTests
{
    [Fact]
    public void Referencing_generated_assemblies_preserves_discovery_and_public_registration()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var b = Generate("ContractsB_" + suffix, Contract("ContractsB"));
        var bImage = Emit(b);
        var a = Generate("ContractsA_" + suffix, Contract("ContractsA"), MetadataReference.CreateFromImage(bImage));
        var aImage = Emit(a);
        var c = GeneratorTestHelper.CreateCompilation("""
            using DotBoxD.Services.Generated;
            public static class Consumer
            {
                public static void Register(IRpcServiceRegistrationSink sink)
                {
                    GeneratedServiceRegistry.RegisterServices(new[] { typeof(ContractsA.IService).Assembly }, sink);
                    GeneratedServiceRegistry.RegisterServices(new[] { typeof(ContractsB.IService).Assembly }, sink);
                }
                public static void RegisterGenerated(IRpcGeneratedServiceRegistrationSink sink)
                {
                    GeneratedServiceRegistry.RegisterGeneratedServices(
                        new[] { typeof(ContractsA.IService).Assembly, typeof(ContractsB.IService).Assembly }, sink);
                }
            }
            """).AddReferences(MetadataReference.CreateFromImage(aImage), MetadataReference.CreateFromImage(bImage));
        AssertNoConflicts(c);
        var bAssembly = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(bImage));
        var aAssembly = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(aImage));
        var cAssembly = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(Emit(c)));
        var sink = new RecordingSink();
        cAssembly.GetType("Consumer")!.GetMethod("Register")!.Invoke(null, new object[] { sink });
        Assert.Equal(new[] { "ContractsA.IService", "ContractsB.IService" }, sink.Services);
        sink.Services.Clear();
        cAssembly.GetType("Consumer")!.GetMethod("RegisterGenerated")!.Invoke(null, new object[] { sink });
        Assert.Equal(new[] { "ContractsA.IService", "ContractsB.IService" }, sink.Services);
        foreach (var assembly in new[] { aAssembly, bAssembly })
        {
            var descriptor = Assert.Single(GeneratedServiceRegistry.GetServices(assembly));
            Assert.Same(assembly, descriptor.ServiceType.Assembly);
            Assert.Same(assembly, descriptor.ProxyType.Assembly);
            Assert.False(assembly.GetType("DotBoxD.Services.Generated.DotBoxDGenerated")!.IsPublic);
        }
    }

    private static string Contract(string ns) => $$"""
        namespace {{ns}}
        {
            [DotBoxD.Services.Attributes.RpcService]
            public interface IService { int Read(); }
        }
        """;

    private static Compilation Generate(string assemblyName, string source, params MetadataReference[] references)
    {
        var input = GeneratorTestHelper.CreateCompilation(source).WithAssemblyName(assemblyName).AddReferences(references);
        GeneratorTestHelper.CreateDriver().RunGeneratorsAndUpdateCompilation(input, out var output, out var diagnostics);
        Assert.Empty(diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        AssertNoConflicts(output);
        return output;
    }

    private static void AssertNoConflicts(Compilation compilation) => Assert.Empty(compilation.GetDiagnostics()
        .Where(d => d.Severity == DiagnosticSeverity.Error || d.Id is "CS0436" or "CS0433"));

    private static byte[] Emit(Compilation compilation)
    {
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        return stream.ToArray();
    }

    private sealed class RecordingSink : IRpcServiceRegistrationSink, IRpcGeneratedServiceRegistrationSink
    {
        public List<string> Services { get; } = new();
        public void AddService<TService, TImplementation>() where TService : class where TImplementation : TService
            => Services.Add(typeof(TService).FullName!);
        public void AddService<TService, TProxy, TDispatcher>() where TService : class where TProxy : TService
            where TDispatcher : DotBoxD.Services.Server.IServiceDispatcher
            => Services.Add(typeof(TService).FullName!);
    }
}
