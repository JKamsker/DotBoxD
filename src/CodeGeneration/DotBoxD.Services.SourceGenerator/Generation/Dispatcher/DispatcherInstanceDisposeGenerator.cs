using System.Text;
using DotBoxD.Services.SourceGenerator.Infrastructure;
using DotBoxD.Services.SourceGenerator.Models;

namespace DotBoxD.Services.SourceGenerator.Generation;

internal static class DispatcherInstanceDisposeGenerator
{
    public static bool IsDisposableDispose(MethodModel method)
    {
        if (method.Name != "Dispose" ||
            method.ReturnKind != MethodReturnKind.Void ||
            method.Parameters.Count != 0)
        {
            return false;
        }

        if (method.ExplicitImplementationType == ServicesGeneratorTypeNames.GlobalIDisposable)
        {
            return true;
        }

        foreach (var implementationType in method.AdditionalExplicitImplementationTypes)
        {
            if (implementationType == ServicesGeneratorTypeNames.GlobalIDisposable)
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsAsyncDisposableDispose(MethodModel method)
    {
        if (method.Name != "DisposeAsync" ||
            method.ReturnKind != MethodReturnKind.ValueTask ||
            method.Parameters.Count != 0)
        {
            return false;
        }

        if (method.ExplicitImplementationType == ServicesGeneratorTypeNames.GlobalIAsyncDisposable)
        {
            return true;
        }

        foreach (var implementationType in method.AdditionalExplicitImplementationTypes)
        {
            if (implementationType == ServicesGeneratorTypeNames.GlobalIAsyncDisposable)
            {
                return true;
            }
        }

        return false;
    }

    public static void GenerateReturn(
        StringBuilder sb,
        ServiceModel service,
        string call,
        string instanceId)
    {
        sb.AppendLine($"                    if ({instanceId} is not null)");
        sb.AppendLine("                    {");
        sb.AppendLine($"                        await registry.{ServicesGeneratorMemberNames.InstanceRegistry.ReleaseAsync}(\"{service.ServiceName}\", {instanceId}).ConfigureAwait(false);");
        sb.AppendLine("                        return;");
        sb.AppendLine("                    }");
        sb.AppendLine();
        sb.AppendLine($"                    var __dotboxd_task = {call};");
        sb.AppendLine("                    if (!__dotboxd_task.IsCompletedSuccessfully)");
        sb.AppendLine("                    {");
        sb.AppendLine("                        await __dotboxd_task;");
        sb.AppendLine("                        return;");
        sb.AppendLine("                    }");
        sb.AppendLine("                    __dotboxd_task.GetAwaiter().GetResult();");
        sb.AppendLine("                    return;");
    }

    public static void GenerateSyncReturn(
        StringBuilder sb,
        ServiceModel service,
        string call,
        string instanceId)
    {
        sb.AppendLine($"                    if ({instanceId} is not null)");
        sb.AppendLine("                    {");
        sb.AppendLine($"                        registry.{ServicesGeneratorMemberNames.InstanceRegistry.Release}(\"{service.ServiceName}\", {instanceId});");
        sb.AppendLine("                        return;");
        sb.AppendLine("                    }");
        sb.AppendLine();
        sb.AppendLine($"                    {call};");
        sb.AppendLine("                    return;");
    }
}
