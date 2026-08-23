using Microsoft.CodeAnalysis;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace DotBoxD.Kernels.Tests.PluginAnalyzer.Generated;

public sealed class PluginServerLiveSettingsRequiredMemberIdentitySurpriseTests
{
    [Fact]
    public void Generated_live_settings_handle_ignores_foreign_required_member_marker()
    {
        var foreignAssembly = System.Reflection.Assembly.Load(ForeignLiveSettingsAssembly().Image);
        var (_, outputCompilation, diagnostics) = PluginServerGenerationTestDriver.RunWithDiagnostics(Source);

        Assert.DoesNotContain(diagnostics, static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        PluginServerGenerationTestDriver.AssertNoCompilationErrors(outputCompilation);

        var assembly = Load(outputCompilation);
        var wire = Activator.CreateInstance(assembly.GetType("Sample.RecordingControlService", throwOnError: true)!)!;
        var serverType = assembly.GetType("Sample.Plugin.RemotePluginServer", throwOnError: true)!;
        var server = Activator.CreateInstance(serverType, [wire, null])!;
        var kernelType = assembly.GetType("Sample.Plugin.FireDamageKernel", throwOnError: true)!;
        var handle = serverType.GetMethod("Get")!.MakeGenericMethod(kernelType).Invoke(server, null)!;
        var isRequired = handle.GetType().GetMethod("IsRequired", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var foreignProperty = foreignAssembly.GetType("Foreign.BaseLiveSettings", throwOnError: true)!
            .GetProperty("OptionalText")!;
        var requiredProperty = assembly.GetType("Sample.Plugin.FireDamageKernel", throwOnError: true)!
            .GetProperty("RequiredText")!;

        Assert.False((bool)isRequired.Invoke(null, [foreignProperty])!);
        Assert.True((bool)isRequired.Invoke(null, [requiredProperty])!);
    }

    private const string Source = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using DotBoxD.Abstractions;
        using DotBoxD.Plugins;
        using DotBoxD.Services.Attributes;

        namespace Sample.Game
        {
            [RpcService]
            public interface IGameWorldAccess;
        }

        namespace Sample.Game.Ipc
        {
            public readonly record struct LiveSettingUpdate(string Name, string Value);

            public interface IGamePluginControlService : DotBoxD.Plugins.IServerExtensionWireClient
            {
                ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default);
                ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default);
                ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default);
                ValueTask UpdateSettingsAsync(string pluginId, LiveSettingUpdate[] updates, bool atomic = false, CancellationToken ct = default);
                ValueTask HoldUntilShutdownAsync(CancellationToken ct = default);
            }
        }

        namespace DotBoxD.Services.Generated
        {
            public static class DotBoxDGeneratedExtensions
            {
                public static Sample.Game.IGameWorldAccess GetGameWorldAccess(DotBoxD.Services.Peer.RpcPeer peer)
                    => throw new InvalidOperationException("not used");
            }
        }

        namespace Sample.Plugin
        {
            using Sample.Game;

            public sealed record DamageEvent(string TargetId);

            [Plugin("fire-damage")]
            public sealed partial class FireDamageKernel : IEventKernel<DamageEvent>
            {
                [LiveSetting]
                public required string RequiredText { get; set; }

                public bool ShouldHandle(DamageEvent e, HookContext ctx) => true;
                public void Handle(DamageEvent e, HookContext ctx) => ctx.Messages.Send(e.TargetId, "handled");
            }

            [GeneratePluginServer(Context = typeof(RemotePluginContext))]
            public partial class RemotePluginServer : IGameWorldAccess;

            public sealed partial class RemotePluginContext;
        }

        namespace Sample
        {
            using Sample.Game.Ipc;

            public sealed class RecordingControlService : IGamePluginControlService
            {
                public ValueTask<string> InstallPluginAsync(string packageJson, CancellationToken ct = default) => ValueTask.FromResult("fire-damage");
                public ValueTask<string> InstallSubscriptionAsync(string packageJson, CancellationToken ct = default) => ValueTask.FromResult("fire-damage");
                public ValueTask<string> InstallServerExtensionAsync(string packageJson, CancellationToken ct = default) => ValueTask.FromResult("fire-damage");
                public ValueTask UpdateSettingsAsync(string pluginId, LiveSettingUpdate[] updates, bool atomic = false, CancellationToken ct = default) => default;
                public ValueTask HoldUntilShutdownAsync(CancellationToken ct = default) => default;
                public ValueTask<byte[]> InvokeServerExtensionAsync(string pluginId, byte[] arguments, CancellationToken cancellationToken = default) => ValueTask.FromResult(Array.Empty<byte>());
            }
        }
        """;

    private static (MetadataReference Reference, byte[] Image) ForeignLiveSettingsAssembly()
    {
        var assembly = AssemblyDefinition.CreateAssembly(
            new AssemblyNameDefinition("ForeignLiveSettings", new Version(1, 0, 0, 0)),
            "ForeignLiveSettings",
            ModuleKind.Dll);
        using (assembly)
        {
            var module = assembly.MainModule;
            var marker = new TypeDefinition(
                "System.Runtime.CompilerServices",
                "RequiredMemberAttribute",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
                module.ImportReference(typeof(Attribute)));
            marker.Methods.Add(Constructor(module, marker));
            module.Types.Add(marker);

            var baseType = new TypeDefinition(
                "Foreign",
                "BaseLiveSettings",
                Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class,
                module.ImportReference(typeof(object)));
            var baseConstructor = Constructor(module, baseType);
            baseConstructor.CustomAttributes.Add(new CustomAttribute(module.ImportReference(typeof(System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute).GetConstructor(Type.EmptyTypes)!)));
            baseType.Methods.Add(baseConstructor);
            AddNullableLiveSetting(module, baseType, marker);
            module.Types.Add(baseType);

            using var stream = new MemoryStream();
            assembly.Write(stream);
            var image = stream.ToArray();
            return (MetadataReference.CreateFromImage(image), image);
        }
    }

    private static MethodDefinition Constructor(ModuleDefinition module, TypeDefinition type)
    {
        var constructor = new MethodDefinition(
            ".ctor",
            Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.HideBySig |
            Mono.Cecil.MethodAttributes.SpecialName | Mono.Cecil.MethodAttributes.RTSpecialName,
            module.TypeSystem.Void);
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Call, module.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes)!)));
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        return constructor;
    }

    private static void AddNullableLiveSetting(ModuleDefinition module, TypeDefinition baseType, TypeDefinition marker)
    {
        var propertyType = module.TypeSystem.String;
        var field = new FieldDefinition("<OptionalText>k__BackingField", Mono.Cecil.FieldAttributes.Private, propertyType);
        baseType.Fields.Add(field);

        var getter = new MethodDefinition("get_OptionalText", Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.SpecialName | Mono.Cecil.MethodAttributes.HideBySig, propertyType);
        getter.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        getter.Body.Instructions.Add(Instruction.Create(OpCodes.Ldfld, field));
        getter.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        baseType.Methods.Add(getter);

        var setter = new MethodDefinition("set_OptionalText", Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.SpecialName | Mono.Cecil.MethodAttributes.HideBySig, module.TypeSystem.Void);
        setter.Parameters.Add(new ParameterDefinition("value", Mono.Cecil.ParameterAttributes.None, propertyType));
        setter.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
        setter.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_1));
        setter.Body.Instructions.Add(Instruction.Create(OpCodes.Stfld, field));
        setter.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        baseType.Methods.Add(setter);

        var property = new PropertyDefinition("OptionalText", Mono.Cecil.PropertyAttributes.None, propertyType)
        {
            GetMethod = getter,
            SetMethod = setter,
        };
        property.CustomAttributes.Add(new CustomAttribute(module.ImportReference(typeof(DotBoxD.Abstractions.LiveSettingAttribute).GetConstructor(Type.EmptyTypes)!)));
        property.CustomAttributes.Add(new CustomAttribute(module.ImportReference(marker.Methods.Single())));
        baseType.Properties.Add(property);
    }

    private static Delegate CreateAction(Type kernelType, string methodName)
    {
        var method = typeof(PluginServerLiveSettingsRequiredMemberIdentitySurpriseTests)
            .GetMethod(methodName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .MakeGenericMethod(kernelType);
        return Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(kernelType), method);
    }

    private static void SetForeignMarkedValue<TKernel>(TKernel kernel) where TKernel : class
        => typeof(TKernel).GetProperty("OptionalText")!.SetValue(kernel, null);

    private static void SetGenuinelyRequiredValue<TKernel>(TKernel kernel) where TKernel : class
        => typeof(TKernel).GetProperty("RequiredText")!.SetValue(kernel, null);

    private static System.Reflection.Assembly Load(Compilation compilation)
    {
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        return System.Reflection.Assembly.Load(stream.ToArray());
    }

    private static async Task<Exception?> CaptureExceptionAsync(Func<Task> action)
    {
        try
        { await action(); return null; }
        catch (System.Reflection.TargetInvocationException exception) when (exception.InnerException is not null) { return exception.InnerException; }
        catch (Exception exception) { return exception; }
    }

    private static async Task AwaitValueTask(object valueTask)
        => await (Task)valueTask.GetType().GetMethod("AsTask", Type.EmptyTypes)!.Invoke(valueTask, null)!;
}
