namespace SafeIR.Hosting;

using System.Security.Cryptography;
using System.Text;
using SafeIR;

internal static class ExecutionPlanBuilder
{
    public static ExecutionPlan Build(
        SandboxModule module,
        SandboxPolicy policy,
        BindingRegistry bindings,
        IReadOnlyDictionary<string, FunctionAnalysis> functions)
    {
        var moduleHash = CanonicalModuleHasher.Hash(module);
        var planHash = Hash("plan-v1", moduleHash, policy.Hash, bindings.ManifestHash);
        return new ExecutionPlan(
            moduleHash,
            planHash,
            policy.Hash,
            bindings.ManifestHash,
            module,
            policy,
            bindings,
            policy.ResourceLimits,
            new ExecutableBytecode(CollectOperations(module)),
            functions);
    }

    private static IReadOnlyList<string> CollectOperations(SandboxModule module)
    {
        var operations = new List<string>();
        foreach (var statement in module.Functions.SelectMany(f => f.Body)) {
            Collect(statement, operations);
        }

        return operations;
    }

    private static void Collect(Statement statement, List<string> operations)
    {
        operations.Add(statement.GetType().Name);
        switch (statement) {
            case IfStatement branch:
                branch.Then.ToList().ForEach(s => Collect(s, operations));
                branch.Else.ToList().ForEach(s => Collect(s, operations));
                break;
            case WhileStatement loop:
                loop.Body.ToList().ForEach(s => Collect(s, operations));
                break;
            case ForRangeStatement range:
                range.Body.ToList().ForEach(s => Collect(s, operations));
                break;
        }
    }

    private static string Hash(params string[] parts)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', parts)))).ToLowerInvariant();
}
