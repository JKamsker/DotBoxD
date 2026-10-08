using System.Collections.Immutable;

namespace DotBoxD.UI.Authoring;

/// <summary>Deterministic worker-side component builder over public schema primitives.</summary>
public sealed class UiBuilder
{
    private readonly List<UiExtension> _extensions = [];
    private readonly List<UiResource> _resources = [];
    private readonly List<UiNode> _nodes = [];
    private readonly List<UiStateSlot> _state = [];
    private readonly List<UiKernel> _kernels = [];
    private readonly List<UiEvent> _events = [];
    private readonly HashSet<int> _endpoints = [];
    private readonly object _elementOwner = new();

    public UiState<int> State(int initial) => State<int>(UiValue.FromInt32(initial));
    public UiState<bool> State(bool initial) => State<bool>(UiValue.FromBoolean(initial));
    public UiState<double> State(double initial) => State<double>(UiValue.FromNumber(initial));
    public UiState<string> State(string initial) => State<string>(UiValue.FromString(initial));
    public UiState<ImmutableArray<UiListItem>> State(ImmutableArray<UiListItem> initial)
        => State<ImmutableArray<UiListItem>>(UiValue.FromItems(initial));

    private UiState<T> State<T>(UiValue initial)
    {
        var id = _state.Count + 1;
        _state.Add(new UiStateSlot(id, initial));
        return new UiState<T>(id);
    }

    public UiBoundKernel<TOutput> Kernel<TInput, TOutput>(UiKernelDefinition<TInput, TOutput> definition, UiState<TInput> input)
        => AddKernel<TOutput>(definition.ModuleJson, definition.Entrypoint, input.Id);

    public UiBoundKernel<TOutput> Kernel<TOutput>(UiKernelDefinition<UiUnit, TOutput> definition)
        => AddKernel<TOutput>(definition.ModuleJson, definition.Entrypoint, 0);

    private UiBoundKernel<T> AddKernel<T>(string moduleJson, string entrypoint, int input)
    {
        var id = _kernels.Count + 1;
        _kernels.Add(new UiKernel(id, moduleJson, entrypoint, input));
        return new UiBoundKernel<T>(id);
    }

    public UiElement Element(UiPrimitive primitive, ImmutableArray<UiProperty> properties, params UiElement[] children)
    {
        ValidateChildren(children);
        var id = _nodes.Count + 1;
        _nodes.Add(new UiNode(id, primitive, [.. children.Select(c => c.Id)], properties));
        return new UiElement(id, _elementOwner);
    }

    private void ValidateChildren(UiElement[] children)
    {
        ArgumentNullException.ThrowIfNull(children);
        foreach (var child in children)
        { ValidateElement(child, nameof(children)); }
    }

    private void ValidateElement(UiElement element, string parameterName)
    {
        if (element is null || !ReferenceEquals(element.Owner, _elementOwner))
        { throw new ArgumentException("UI elements must be created by this builder.", parameterName); }
    }

    public UiElement Extension(string schemaId, string payloadJson)
    {
        var element = Element(UiPrimitive.Extension, []);
        _extensions.Add(new UiExtension(element.Id, schemaId, payloadJson));
        return element;
    }

    public int Resource(string handle)
    {
        var existing = _resources.FirstOrDefault(r => string.Equals(r.Handle, handle, StringComparison.Ordinal));
        if (existing is not null)
        { return existing.Id; }
        var id = _resources.Count + 1;
        _resources.Add(new UiResource(id, handle));
        return id;
    }

    public UiElement Image(int resourceId, string alternativeText = "") => Element(UiPrimitive.Image,
        [UiLiteral.Integer(resourceId).Property(UiPropertyId.Resource), UiLiteral.Text(alternativeText).Property(UiPropertyId.Text)]);

    public UiElement Stack(params UiElement[] children) => Element(UiPrimitive.Stack, [], children);
    public UiElement Grid(int columns, params UiElement[] children)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(columns);
        ValidateChildren(children);
        for (var index = 0; index < children.Length; index++)
        {
            var id = children[index].Id;
            var node = _nodes[id - 1];
            if (node.Properties.Any(p => p.Id == UiPropertyId.Row || p.Id == UiPropertyId.Column))
            { continue; }
            _nodes[id - 1] = node with
            {
                Properties = [.. node.Properties,
                UiLiteral.Integer(index / columns).Property(UiPropertyId.Row),
                UiLiteral.Integer(index % columns).Property(UiPropertyId.Column)]
            };
        }
        return Element(UiPrimitive.Grid, [UiLiteral.Integer(columns).Property(UiPropertyId.Columns)], children);
    }
    public UiElement Border(UiElement child, double padding = 0)
        => Element(UiPrimitive.Border, [UiLiteral.Number(padding).Property(UiPropertyId.Padding)], child);
    public UiElement Scroll(UiElement child) => Element(UiPrimitive.ScrollViewer, [], child);
    public UiElement Text(UiBinding<string> value) => Element(UiPrimitive.Text, [value.Property(UiPropertyId.Text)]);
    public UiElement Text(string text) => Text(UiLiteral.Text(text));
    public UiElement TextBox(UiState<string> value)
        => Element(UiPrimitive.TextBox, [UiBinding<string>.Input(value).Property(UiPropertyId.Text)]);
    public UiElement CheckBox(string label, UiState<bool> value)
        => Element(UiPrimitive.CheckBox, [UiLiteral.Text(label).Property(UiPropertyId.Text),
            UiBinding<bool>.Input(value).Property(UiPropertyId.Checked)]);
    public UiElement Slider(UiState<double> value, double maximum = 100)
        => Element(UiPrimitive.Slider, [UiBinding<double>.Input(value).Property(UiPropertyId.Value),
            UiLiteral.Number(maximum).Property(UiPropertyId.Maximum)]);
    public UiElement Progress(UiBinding<double> value, double maximum = 100)
        => Element(UiPrimitive.ProgressBar, [value.Property(UiPropertyId.Value),
            UiLiteral.Number(maximum).Property(UiPropertyId.Maximum)]);
    public UiElement Items(UiBinding<ImmutableArray<UiListItem>> value)
        => Element(UiPrimitive.Items, [value.Property(UiPropertyId.Items)]);

    public UiElement Button<T>(string label, UiBoundKernel<T> handler, UiState<T> output)
    {
        var button = Element(UiPrimitive.Button, [UiLiteral.Text(label).Property(UiPropertyId.Text)]);
        return Handle(button, handler, output);
    }

    public UiElement Handle<T>(UiElement button, UiBoundKernel<T> handler, UiState<T> output)
    {
        ValidateElement(button, nameof(button));
        _events.Add(new UiEvent(_events.Count + 1, button.Id, UiEventKind.Click, UiEventTarget.LocalKernel,
            KernelId: handler.Id, OutputSlotId: output.Id));
        return button;
    }

    public UiElement RemoteButton(string label, int endpointId)
    {
        var button = Element(UiPrimitive.Button, [UiLiteral.Text(label).Property(UiPropertyId.Text)]);
        return Remote(button, endpointId);
    }

    public UiElement Remote(UiElement button, int endpointId)
    {
        ValidateElement(button, nameof(button));
        _endpoints.Add(endpointId);
        _events.Add(new UiEvent(_events.Count + 1, button.Id, UiEventKind.Click, UiEventTarget.Remote,
            RemoteEndpointId: endpointId));
        return button;
    }

    public UiPackage Build(UiElement root, UiPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ValidateElement(root, nameof(root));
        var package = new UiPackage(UiPackage.CurrentFormatVersion, root.Id, [.. _nodes], [.. _state],
            [.. _kernels], [.. _events], [.. _endpoints.Order()])
        { Resources = [.. _resources], Extensions = [.. _extensions] };
        UiPackageValidator.Validate(package, policy ?? new UiPolicy());
        return package;
    }
}
