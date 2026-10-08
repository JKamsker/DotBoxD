using DotBoxD.UI.Runtime;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DotBoxD.UI.Blazor.Tests;

internal sealed class TestBlazorRenderer() : Renderer(new ServiceCollection().BuildServiceProvider(), NullLoggerFactory.Instance)
{
    private int _componentId;
    public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
    public bool MovedKeyedRows { get; private set; }
    public List<Exception> Errors { get; } = [];

    public Task MountAsync(UiSession session, IUiInteractionAuthorizer? authorizer = null, bool dispose = true)
        => Dispatcher.InvokeAsync(async () =>
        {
            _componentId = AssignRootComponentId(new DotBoxDUi());
            await RenderRootComponentAsync(_componentId, Parameters(session, authorizer, dispose));
        });

    public Task ReplaceAsync(UiSession session, IUiInteractionAuthorizer? authorizer, bool dispose = true)
        => Dispatcher.InvokeAsync(() => RenderRootComponentAsync(_componentId, Parameters(session, authorizer, dispose)));

    private static ParameterView Parameters(UiSession session, IUiInteractionAuthorizer? authorizer, bool dispose)
        => ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(DotBoxDUi.Session)] = session,
            [nameof(DotBoxDUi.User)] = UiFixture.User(),
            [nameof(DotBoxDUi.Authorizer)] = authorizer,
            [nameof(DotBoxDUi.DisposeSessionOnDetach)] = dispose
        });

    public Task<RenderTreeFrame[]> FramesAsync() => Dispatcher.InvokeAsync(() =>
    {
        var frames = GetCurrentRenderTreeFrames(_componentId);
        return frames.Array.Take(frames.Count).ToArray();
    });

    public async Task<ulong> HandlerAsync(string name)
        => (await FramesAsync()).Single(f => f.FrameType == RenderTreeFrameType.Attribute && f.AttributeName == name).AttributeEventHandlerId;

    public Task SendAsync(ulong id, EventArgs args) => Dispatcher.InvokeAsync(() => DispatchEventAsync(id, null, args));
    public Task SendAsync(string name, EventArgs args) => Dispatcher.InvokeAsync(() =>
    {
        var frames = GetCurrentRenderTreeFrames(_componentId);
        var id = frames.Array.Take(frames.Count).Single(f => f.FrameType == RenderTreeFrameType.Attribute && f.AttributeName == name).AttributeEventHandlerId;
        return DispatchEventAsync(id, null, args);
    });

    protected override Task UpdateDisplayAsync(in RenderBatch renderBatch)
    {
        foreach (var diff in renderBatch.UpdatedComponents.Array.Take(renderBatch.UpdatedComponents.Count))
        {
            if (diff.Edits.Array.Skip(diff.Edits.Offset).Take(diff.Edits.Count).Any(e => e.Type == RenderTreeEditType.PermutationListEntry))
            { MovedKeyedRows = true; }
        }
        return Task.CompletedTask;
    }

    protected override void HandleException(Exception exception) => Errors.Add(exception);
}
