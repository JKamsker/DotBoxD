using System.Collections.Immutable;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels.Policies;
using DotBoxD.UI.Authoring;
using DotBoxD.UI.Runtime;

namespace DotBoxD.UI.Avalonia.Tests.Input;

public sealed class AvaloniaInputOrderingTests
{
    [AvaloniaTheory]
    [InlineData("Text", false)]
    [InlineData("Text", true)]
    [InlineData("Checked", false)]
    [InlineData("Value", false)]
    public async Task Earlier_input_echoes_preserve_newer_native_edits(string kind, bool rejectFirst)
    {
        var b = new UiBuilder();
        var package = kind switch
        {
            "Text" => b.Build(b.TextBox(b.State(""))),
            "Checked" => b.Build(b.CheckBox("On", b.State(false))),
            _ => b.Build(b.Slider(b.State(0d)))
        };
        var renderer = new GatedInputRenderer();
        using var sandbox = SandboxHost.Create(h => h.AddDefaultPureBindings());
        await using var session = await new UiHost(sandbox, SandboxPolicyBuilder.Create().Build(), new UiPolicy { MaxStringLength = 4 })
            .InstallAsync(package, renderer);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var control = renderer.Inner.Root!;
        try
        {
            if (rejectFirst)
            { ((TextBox)control).Text = "oversized"; }
            else
            { SetFirst(control); }
            await renderer.FirstEntered.Task.WaitAsync(timeout.Token);
            SetSecond(control);
            renderer.ReleaseFirst.TrySetResult();
            await renderer.SecondEntered.Task.WaitAsync(timeout.Token);
            AppendThird(control);
            renderer.ReleaseSecond.TrySetResult();
            UiSnapshot snapshot;
            do
            { snapshot = await session.SnapshotAsync(timeout.Token); await Task.Yield(); }
            while (snapshot.Version < (rejectFirst ? 2 : 3));
            var expected = kind switch
            {
                "Text" => UiValue.FromString("abc"),
                "Checked" => UiValue.FromBoolean(true),
                _ => UiValue.FromNumber(21)
            };
            Assert.Equal(expected, snapshot.State[0].Value);
            Assert.Equal(expected, Read(control));
            Assert.False(session.IsDisconnected);
        }
        finally
        {
            renderer.ReleaseFirst.TrySetResult();
            renderer.ReleaseSecond.TrySetResult();
        }
    }

    private static void SetFirst(Control control)
    {
        switch (control)
        {
            case TextBox box:
                box.Text = "a";
                break;
            case CheckBox check:
                check.IsChecked = true;
                break;
            case Slider slider:
                slider.Value = 10;
                break;
        }
    }

    private static void SetSecond(Control control)
    {
        switch (control)
        {
            case TextBox box:
                box.Text = "ab";
                break;
            case CheckBox check:
                check.IsChecked = false;
                break;
            case Slider slider:
                slider.Value = 20;
                break;
        }
    }

    private static void AppendThird(Control control)
    {
        switch (control)
        {
            case TextBox box:
                box.Text += "c";
                break;
            case CheckBox check:
                check.IsChecked = !check.IsChecked.GetValueOrDefault();
                break;
            case Slider slider:
                slider.Value++;
                break;
        }
    }

    private static UiValue Read(Control control) => control switch
    {
        TextBox box => UiValue.FromString(box.Text!),
        CheckBox check => UiValue.FromBoolean(check.IsChecked.GetValueOrDefault()),
        Slider slider => UiValue.FromNumber(slider.Value),
        _ => throw new InvalidOperationException()
    };

    private sealed class GatedInputRenderer : IUiRenderer, IUiInputSource
    {
        private int _updates;
        public AvaloniaUiRenderer Inner { get; } = new();
        public TaskCompletionSource FirstEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirst { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseSecond { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<UiInput> ReadAsync(CancellationToken cancellationToken) => Inner.ReadAsync(cancellationToken);
        public ValueTask AcknowledgeAsync(UiInput input, CancellationToken cancellationToken) => Inner.AcknowledgeAsync(input, cancellationToken);
        public ValueTask MaterializeAsync(UiPackage package, ImmutableArray<UiPropertyValue> values, CancellationToken cancellationToken)
            => Inner.MaterializeAsync(package, values, cancellationToken);
        public async ValueTask UpdateAsync(ImmutableArray<UiPropertyValue> changes, CancellationToken cancellationToken)
        {
            switch (Interlocked.Increment(ref _updates))
            {
                case 1:
                    FirstEntered.TrySetResult();
                    await ReleaseFirst.Task.WaitAsync(cancellationToken);
                    break;
                case 2:
                    SecondEntered.TrySetResult();
                    await ReleaseSecond.Task.WaitAsync(cancellationToken);
                    break;
            }
            await Inner.UpdateAsync(changes, cancellationToken);
        }
        public ValueTask DisposeAsync() => Inner.DisposeAsync();
    }
}
