using System.Text;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using DotBoxD.Services.SourceGenerator.Models;

namespace DotBoxD.Services.SourceGenerator.Generation;

internal static class DispatcherSubServiceReturnGenerator
{
    public static void Generate(StringBuilder sb, MethodModel method, string call)
    {
        var info = method.SubService!;
        if (method.ReturnKind == MethodReturnKind.SyncSubService)
        {
            sb.AppendLine($"                    var __sub = {call};");
        }
        else
        {
            GenerateAwaitedSubService(sb, call);
        }

        GenerateCancellationCleanup(sb);

        if (info.AllowsNull)
        {
            sb.AppendLine("                    if (__sub is null)");
            sb.AppendLine("                    {");
            AppendCancellationCheckpoint(sb, "                        ");
            sb.AppendLine($"                        serializer.{ServicesGeneratorMemberNames.Serializer.Serialize}<{ServicesGeneratorTypeNames.NullableOf(ServicesGeneratorTypeNames.GlobalServiceHandle)}>(output, null);");
            sb.AppendLine("                        return;");
            sb.AppendLine("                    }");
        }

        sb.AppendLine("                    string __subId;");
        sb.AppendLine("                    try");
        sb.AppendLine("                    {");
        sb.AppendLine($"                        __subId = registry.{ServicesGeneratorMemberNames.InstanceRegistry.Register}(\"{info.ServiceName}\", __sub);");
        sb.AppendLine("                    }");
        sb.AppendLine("                    catch");
        sb.AppendLine("                    {");
        GenerateSubServiceCleanup(sb);
        sb.AppendLine("                        throw;");
        sb.AppendLine("                    }");
        GenerateRegisteredCancellationCleanup(sb, info.ServiceName);
        GenerateSubServiceHandleSerialization(sb, info.ServiceName);
    }

    private static void GenerateCancellationCleanup(StringBuilder sb)
    {
        sb.AppendLine("                    if (ct.IsCancellationRequested)");
        sb.AppendLine("                    {");
        GenerateSubServiceCleanup(sb);
        sb.AppendLine("                        ct.ThrowIfCancellationRequested();");
        sb.AppendLine("                    }");
    }

    private static void GenerateAwaitedSubService(StringBuilder sb, string call)
    {
        sb.AppendLine($"                    var __dotboxd_task = {call};");
        sb.AppendLine("                    var __sub = __dotboxd_task.IsCompletedSuccessfully");
        sb.AppendLine("                        ? __dotboxd_task.Result");
        sb.AppendLine("                        : await __dotboxd_task;");
    }

    private static void GenerateRegisteredCancellationCleanup(StringBuilder sb, string serviceName)
    {
        sb.AppendLine("                    if (ct.IsCancellationRequested)");
        sb.AppendLine("                    {");
        sb.AppendLine("                        try");
        sb.AppendLine("                        {");
        sb.AppendLine($"                            await registry.{ServicesGeneratorMemberNames.InstanceRegistry.ReleaseAsync}(\"{serviceName}\", __subId).ConfigureAwait(false);");
        sb.AppendLine("                        }");
        sb.AppendLine("                        catch");
        sb.AppendLine("                        {");
        sb.AppendLine("                            // Best-effort release: a faulting release must not replace");
        sb.AppendLine("                            // the cancellation that is about to be thrown.");
        sb.AppendLine("                        }");
        sb.AppendLine("                        ct.ThrowIfCancellationRequested();");
        sb.AppendLine("                    }");
    }

    private static void GenerateSubServiceCleanup(StringBuilder sb)
    {
        sb.AppendLine("                        try");
        sb.AppendLine("                        {");
        sb.AppendLine($"                            if (__sub is {ServicesGeneratorTypeNames.GlobalIAsyncDisposable} __ad)");
        sb.AppendLine("                            {");
        sb.AppendLine("                                await __ad.DisposeAsync().ConfigureAwait(false);");
        sb.AppendLine("                            }");
        sb.AppendLine($"                            else if (__sub is {ServicesGeneratorTypeNames.GlobalIDisposable} __d)");
        sb.AppendLine("                            {");
        sb.AppendLine("                                __d.Dispose();");
        sb.AppendLine("                            }");
        sb.AppendLine("                        }");
        sb.AppendLine("                        catch");
        sb.AppendLine("                        {");
        sb.AppendLine("                            // Best-effort cleanup of the orphaned sub-service: a faulting disposer must");
        sb.AppendLine("                            // not replace the original registration failure that is about to be rethrown.");
        sb.AppendLine("                        }");
    }

    private static void GenerateSubServiceHandleSerialization(StringBuilder sb, string serviceName)
    {
        sb.AppendLine("                    try");
        sb.AppendLine("                    {");
        AppendCancellationCheckpoint(sb, "                        ");
        sb.AppendLine($"                        serializer.{ServicesGeneratorMemberNames.Serializer.Serialize}(output, new {ServicesGeneratorTypeNames.GlobalServiceHandle} {{ {ServicesGeneratorMemberNames.ServiceHandle.ServiceName} = \"{serviceName}\", {ServicesGeneratorMemberNames.ServiceHandle.InstanceId} = __subId }});");
        sb.AppendLine("                    }");
        sb.AppendLine("                    catch");
        sb.AppendLine("                    {");
        sb.AppendLine("                        try");
        sb.AppendLine("                        {");
        sb.AppendLine($"                            await registry.{ServicesGeneratorMemberNames.InstanceRegistry.ReleaseAsync}(\"{serviceName}\", __subId).ConfigureAwait(false);");
        sb.AppendLine("                        }");
        sb.AppendLine("                        catch");
        sb.AppendLine("                        {");
        sb.AppendLine("                            // Best-effort release: a faulting release must not replace");
        sb.AppendLine("                            // the original serialization failure that is about to be rethrown.");
        sb.AppendLine("                        }");
        sb.AppendLine("                        throw;");
        sb.AppendLine("                    }");
        sb.AppendLine("                    return;");
    }

    private static void AppendCancellationCheckpoint(StringBuilder sb, string indent = "                    ")
    {
        sb.Append(indent).AppendLine("ct.ThrowIfCancellationRequested();");
    }
}
