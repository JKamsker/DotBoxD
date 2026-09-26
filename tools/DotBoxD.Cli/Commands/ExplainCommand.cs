using System.Text;
using System.Text.Json;
using DotBoxD.Cli.Infrastructure;
using DotBoxD.Kernels;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Runtime.Bindings;
using DotBoxD.Kernels.Serialization.Json;
using DotBoxD.Plugins.Inspection;
using DotBoxD.Plugins.Json;
using DotBoxD.Plugins.Replay;

namespace DotBoxD.Cli.Commands;

internal static class ExplainCommand
{
    public static async Task<CommandResult> RunAsync(string path, CancellationToken cancellationToken)
    {
        var text = await InputFile.ReadAsync(path, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(text);
        SandboxModule module;
        SandboxPolicy policy;
        BindingRegistry bindings;
        if (document.RootElement.TryGetProperty("FormatVersion", out _))
        {
            var trace = ExecutionTrace.Deserialize(text);
            module = JsonImporter.Import(trace.ModuleJson);
            policy = trace.Policy.Restore();
            bindings = new BindingRegistry(trace.Bindings.Select(signature => new BindingDescriptor(
                signature.Id, signature.Version, signature.Parameters, signature.ReturnType, signature.Effects,
                signature.RequiredCapability, signature.CostModel, signature.AuditLevel, signature.Safety,
                (_, _, _) => throw new InvalidOperationException("Inspection does not execute bindings."), signature.Compiled)
            {
                IsAsync = signature.IsAsync,
                AuditKind = signature.AuditKind
            }));
        }
        else
        {
            module = document.RootElement.TryGetProperty("manifest", out _)
                ? PluginPackageJsonSerializer.Import(text).Module : JsonImporter.Import(text);
            policy = SandboxPolicyBuilder.Create().Build();
            bindings = new BindingRegistryBuilder().AddDefaultPureBindings().Build();
        }
        var inspection = PluginInspection.Explain(module, bindings, policy);
        var human = new StringBuilder().AppendLine($"Module: {inspection.ModuleId}")
            .AppendLine($"Validation: {(inspection.Valid ? "accepted" : "rejected")}")
            .AppendLine($"Fuel limit: {inspection.Limits.MaxFuel}")
            .AppendLine("Capability | Decision");
        foreach (var capability in inspection.Capabilities)
        {
            human.AppendLine($"{capability.Capability} | {capability.Explanation}");
        }
        human.AppendLine("Binding | Base fuel | Async");
        foreach (var binding in inspection.Bindings)
        {
            human.AppendLine($"{binding.Id} | {binding.CostModel.BaseFuel} | {binding.IsAsync}");
        }
        foreach (var diagnostic in inspection.Diagnostics)
        {
            human.AppendLine($"{diagnostic.Code}: {diagnostic.Message}");
        }
        human.AppendLine("Lowered IR:").Append(inspection.LoweredJson);
        return new CommandResult(inspection.Valid, inspection, human.ToString());
    }
}
