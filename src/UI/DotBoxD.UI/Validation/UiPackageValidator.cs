using System.Collections.Immutable;
using System.Text;

namespace DotBoxD.UI;

public static class UiPackageValidator
{
    public static void Validate(UiPackage package, UiPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(policy);
        policy.Validate();
        if (package.FormatVersion != UiPackage.CurrentFormatVersion)
        {
            throw new UiValidationException("Unsupported UI package format version.");
        }

        var nodes = Index(package.Nodes, policy.MaxNodes, n => n.Id);
        ValidateFeatures(package.RequiredFeatures);
        ValidateFeatures(package.OptionalFeatures);
        var state = Index(package.State, policy.MaxStateSlots, s => s.Id);
        var kernels = Index(package.Kernels, policy.MaxKernels, k => k.Id);
        var events = Index(package.Events, policy.MaxEvents, e => e.Id);
        ValidateEndpoints(package.RemoteEndpoints, policy);
        ValidateState(state, policy);
        ValidateKernels(kernels, state, policy);
        UiTreeValidator.Validate(package.RootNodeId, nodes, state, kernels, policy);
        ValidateEvents(package, events, nodes, state, kernels);
    }

    private static void ValidateFeatures(ImmutableArray<UiFeature> features)
    {
        if (features.IsDefault || features.Length > Enum.GetValues<UiFeature>().Length ||
            features.Any(f => !Enum.IsDefined(f)) || features.Distinct().Count() != features.Length)
        { throw new UiValidationException("Unsupported or duplicate UI feature declaration."); }
    }

    private static void ValidateEndpoints(ImmutableArray<int> endpoints, UiPolicy policy)
    {
        if (endpoints.IsDefault || endpoints.Length > policy.MaxEvents || endpoints.Any(id => id <= 0) ||
            endpoints.Distinct().Count() != endpoints.Length)
        {
            throw new UiValidationException("Invalid remote endpoint IDs or endpoint limit exceeded.");
        }
    }

    private static void ValidateState(Dictionary<int, UiStateSlot> state, UiPolicy policy)
    {
        long bytes = 0;
        foreach (var slot in state.Values)
        {
            bytes += UiValueValidator.Validate(slot.InitialValue, policy);
        }

        if (bytes > policy.MaxStateBytes)
        {
            throw new UiValidationException("UI state exceeds the host byte limit.");
        }
    }

    private static void ValidateKernels(Dictionary<int, UiKernel> kernels, Dictionary<int, UiStateSlot> state, UiPolicy policy)
    {
        long bytes = 0;
        foreach (var kernel in kernels.Values)
        {
            ValidateKernelSource(kernel, state, policy);
            var length = Encoding.UTF8.GetByteCount(kernel.ModuleJson);
            if (length > policy.MaxKernelBytes)
            {
                throw new UiValidationException("UI kernel exceeds the host byte limit.");
            }

            bytes += length;
        }

        if (bytes > policy.MaxPackageBytes)
        {
            throw new UiValidationException("UI kernels exceed the package byte limit.");
        }
    }

    private static void ValidateKernelSource(UiKernel kernel, Dictionary<int, UiStateSlot> state, UiPolicy policy)
    {
        if (string.IsNullOrWhiteSpace(kernel.ModuleJson) || string.IsNullOrWhiteSpace(kernel.Entrypoint) ||
            kernel.Entrypoint.Length > policy.MaxStringLength || kernel.InputSlotId < 0 ||
            (kernel.InputSlotId != 0 && !state.ContainsKey(kernel.InputSlotId)))
        {
            throw new UiValidationException("Invalid UI kernel or unknown input state slot.");
        }
    }

    private static void ValidateEvents(UiPackage package, Dictionary<int, UiEvent> events,
        Dictionary<int, UiNode> nodes, Dictionary<int, UiStateSlot> state, Dictionary<int, UiKernel> kernels)
    {
        var routes = new HashSet<(int, UiEventKind)>();
        foreach (var route in events.Values)
        {
            if (!nodes.TryGetValue(route.NodeId, out var node) || node.Primitive != UiPrimitive.Button ||
                route.Kind != UiEventKind.Click || !routes.Add((route.NodeId, route.Kind)))
            {
                throw new UiValidationException("Unknown, duplicate or unsupported UI event route.");
            }

            if (!ValidTarget(route, package.RemoteEndpoints, state, kernels))
            {
                throw new UiValidationException("Invalid local/remote UI event target.");
            }
        }
    }

    private static bool ValidTarget(UiEvent route, ImmutableArray<int> endpoints,
        Dictionary<int, UiStateSlot> state, Dictionary<int, UiKernel> kernels)
        => route.Target switch
        {
            UiEventTarget.LocalKernel => kernels.ContainsKey(route.KernelId) &&
                state.ContainsKey(route.OutputSlotId) && route.RemoteEndpointId == 0,
            UiEventTarget.Remote => endpoints.Contains(route.RemoteEndpointId) && route.KernelId == 0 && route.OutputSlotId == 0,
            _ => false
        };

    private static Dictionary<int, T> Index<T>(ImmutableArray<T> items, int maximum, Func<T, int> id) where T : class
    {
        if (items.IsDefault || items.Length > maximum)
        {
            throw new UiValidationException("Missing UI collection or host collection limit exceeded.");
        }

        var result = new Dictionary<int, T>(items.Length);
        foreach (var item in items)
        {
            if (item is null || id(item) <= 0 || !result.TryAdd(id(item), item))
            {
                throw new UiValidationException("UI IDs must be positive and unique within their collection.");
            }
        }

        return result;
    }

}
