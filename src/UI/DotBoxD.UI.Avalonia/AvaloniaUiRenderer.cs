using System.Collections.Immutable;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DotBoxD.UI.Runtime;

namespace DotBoxD.UI.Avalonia;

/// <summary>
/// Trusted host adapter. Root and capture APIs are host-only toolkit integration hooks; they never
/// appear in the worker protocol. Supply Root to a host-owned TopLevel for focus and native input.
/// </summary>
public sealed class AvaloniaUiRenderer : IUiRenderer, IUiInputSource
{
    private readonly Dictionary<int, Control> _controls = [];
    private readonly Dictionary<int, UiKeyedRows> _lists = [];
    private readonly UiInputQueue _queue;
    private readonly UiControlInput _input;
    private Control? _root;
    private bool _disposed;
    private bool _installed;

    public AvaloniaUiRenderer(int inputCapacity = 128)
    {
        _queue = new UiInputQueue(inputCapacity);
        _input = new UiControlInput(_queue);
    }

    /// <summary>UI-thread-only access for the trusted compositor/window owner.</summary>
    public Control? Root { get { Dispatcher.UIThread.VerifyAccess(); return _root; } }
    public bool IsDisposed => Volatile.Read(ref _disposed);
    public int Materializations { get; private set; }
    public int Updates { get; private set; }

    public ValueTask<UiInput> ReadAsync(CancellationToken cancellationToken) => _queue.ReadAsync(cancellationToken);

    public async ValueTask MaterializeAsync(UiPackage package, ImmutableArray<UiPropertyValue> values, CancellationToken cancellationToken)
    {
        await Dispatcher.UIThread.InvokeAsync(() => Materialize(package, values), DispatcherPriority.Normal, cancellationToken);
    }

    private void Materialize(UiPackage package, ImmutableArray<UiPropertyValue> values)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_installed)
        { throw new InvalidOperationException("An Avalonia renderer owns one UI session."); }
        _installed = true;
        foreach (var node in package.Nodes)
        {
            var control = UiControlFactory.Create(node.Primitive);
            _controls.Add(node.Id, control);
            if (node.Primitive == UiPrimitive.Items)
            { _lists.Add(node.Id, new UiKeyedRows((StackPanel)control)); }
            _input.Attach(node, control, package.Events);
        }
        foreach (var node in package.Nodes)
        {
            foreach (var child in node.Children)
            { UiControlFactory.Attach(_controls[node.Id], _controls[child]); }
        }
        Apply(values);
        _root = _controls[package.RootNodeId];
        Materializations++;
    }

    public async ValueTask UpdateAsync(ImmutableArray<UiPropertyValue> changes, CancellationToken cancellationToken)
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Apply(changes);
            Updates++;
        }, DispatcherPriority.Normal, cancellationToken);
    }

    private void Apply(ImmutableArray<UiPropertyValue> changes)
    {
        _input.Applying = true;
        try
        {
            // Set ranges/layout before values, avoiding toolkit coercion against old maxima.
            foreach (var change in changes.OrderBy(c => c.PropertyId == UiPropertyId.Value ? 1 : 0))
            {
                if (change.PropertyId == UiPropertyId.Items)
                { _lists[change.NodeId].Update(change.Value.Items); }
                else
                { UiControlProperties.Apply(_controls[change.NodeId], change.PropertyId, change.Value); }
            }
        }
        finally { _input.Applying = false; }
    }

    /// <summary>Trusted offscreen CPU snapshot. The caller owns/disposes the bitmap and composites it.</summary>
    public async ValueTask<RenderTargetBitmap> CaptureAsync(PixelSize pixels, Vector dpi, CancellationToken cancellationToken = default)
    {
        if (pixels.Width <= 0 || pixels.Height <= 0 || (long)pixels.Width * pixels.Height > 16_777_216 ||
            !double.IsFinite(dpi.X) || !double.IsFinite(dpi.Y) || dpi.X <= 0 || dpi.Y <= 0)
        { throw new ArgumentOutOfRangeException(nameof(pixels)); }
        return await Dispatcher.UIThread.InvokeAsync(() =>
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var root = _root ?? throw new InvalidOperationException("UI is not materialized.");
            var size = new Size(pixels.Width * 96 / dpi.X, pixels.Height * 96 / dpi.Y);
            root.Measure(size);
            root.Arrange(new Rect(size));
            var bitmap = new RenderTargetBitmap(pixels, dpi);
            try
            { bitmap.Render(root); return bitmap; }
            catch { bitmap.Dispose(); throw; }
        }, DispatcherPriority.Normal, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_disposed)
            { return; }
            _disposed = true;
            _queue.Complete();
            _input.Clear();
            foreach (var list in _lists.Values)
            { list.Clear(); }
            foreach (var control in _controls.Values)
            { UiControlFactory.Detach(control); }
            _lists.Clear();
            _controls.Clear();
            _root = null;
        });
    }
}
