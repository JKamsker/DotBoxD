using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json;

namespace DotBoxD.UI.Runtime;

internal sealed class UiKernelRunner(SandboxHost host, SandboxExecutionOptions options)
{
    private readonly Dictionary<int, PreparedKernel> _kernels = [];

    public async ValueTask PrepareAsync(UiPackage package, SandboxPolicy policy, CancellationToken cancellationToken)
    {
        var slots = package.State.ToDictionary(s => s.Id, s => s.InitialValue.Kind);
        foreach (var kernel in package.Kernels)
        {
            var module = JsonImporter.Import(kernel.ModuleJson);
            var plan = await host.PrepareAsync(module, policy, cancellationToken).ConfigureAwait(false);
            var function = module.Functions.SingleOrDefault(f => f.Id == kernel.Entrypoint && f.IsEntrypoint)
                ?? throw new UiValidationException("UI kernel must reference a declared entrypoint.");
            if ((kernel.InputSlotId == 0 && function.Parameters.Count != 0) ||
                (kernel.InputSlotId != 0 && (function.Parameters.Count != 1 ||
                    function.Parameters[0].Type != Type(slots[kernel.InputSlotId]))))
            {
                throw new UiValidationException("UI kernel input must match its scalar state slot (or have no parameters).");
            }

            RequireScalarResult(function.ReturnType);
            _kernels.Add(kernel.Id, new PreparedKernel(kernel, plan, function.ReturnType));
        }

        foreach (var route in package.Events.Where(e => e.Target == UiEventTarget.LocalKernel))
        {
            RequireResultType(route.KernelId, slots[route.OutputSlotId]);
        }

        foreach (var node in package.Nodes)
        {
            foreach (var property in node.Properties.Where(p => p.BindingKernelId != 0))
            {
                RequireResultType(property.BindingKernelId, UiValueValidator.PropertyKind(node.Primitive, property.Id));
            }
        }
    }

    public async ValueTask<UiValue> ExecuteAsync(
        int id,
        IReadOnlyDictionary<int, UiValue> state,
        CancellationToken cancellationToken)
    {
        var kernel = _kernels[id];
        var input = kernel.Definition.InputSlotId == 0
            ? SandboxValue.Unit
            : ToSandbox(state[kernel.Definition.InputSlotId]);
        var result = await host.ExecuteAsync(
            kernel.Plan,
            kernel.Definition.Entrypoint,
            input,
            options,
            cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            throw new UiValidationException($"UI kernel failed: {result.Error?.Code}.");
        }

        return result.Value switch
        {
            BoolValue value => UiValue.FromBoolean(value.Value),
            I32Value value => UiValue.FromInt32(value.Value),
            F64Value value => UiValue.FromNumber(value.Value),
            StringValue value => UiValue.FromString(value.Value),
            _ => throw new UiValidationException("UI kernel returned an unsupported value.")
        };
    }

    public void Clear() => _kernels.Clear();

    private static void RequireScalarResult(SandboxType type)
    {
        if (type != SandboxType.Bool && type != SandboxType.I32 && type != SandboxType.F64 && type != SandboxType.String)
        { throw new UiValidationException("Every UI kernel must return a supported scalar, including unused kernels."); }
    }

    private void RequireResultType(int id, UiValueKind kind)
    {
        if (_kernels[id].ResultType != Type(kind))
        {
            throw new UiValidationException("UI kernel return type does not match its target.");
        }
    }

    private static SandboxType Type(UiValueKind kind) => kind switch
    {
        UiValueKind.Boolean => SandboxType.Bool,
        UiValueKind.Int32 => SandboxType.I32,
        UiValueKind.Number => SandboxType.F64,
        UiValueKind.String => SandboxType.String,
        _ => throw new UiValidationException("Unsupported UI scalar type.")
    };

    private static SandboxValue ToSandbox(UiValue value) => value.Kind switch
    {
        UiValueKind.Boolean => SandboxValue.FromBool(value.Boolean),
        UiValueKind.Int32 => SandboxValue.FromInt32(value.Integer),
        UiValueKind.Number => SandboxValue.FromDouble(value.Number),
        UiValueKind.String => SandboxValue.FromString(value.Text),
        _ => throw new UiValidationException("Unsupported UI scalar type.")
    };

    private sealed record PreparedKernel(UiKernel Definition, ExecutionPlan Plan, SandboxType ResultType);
}
