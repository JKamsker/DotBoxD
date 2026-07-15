using DotBoxD.Kernels.Debugging;
using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter.Debugging;

internal sealed class InterpreterDebugState
{
    private readonly ISandboxExecutionDebugHook _hook;
    private readonly SandboxNodeMap _nodes;
    private readonly SandboxContext _context;
    private readonly Dictionary<InterpreterFrame, InterpreterDebugFrame> _frames =
        new(ReferenceEqualityComparer.Instance);
    private SandboxNodeDescriptor? _currentNode;
    private SandboxRuntimeException? _reportedException;
    private InterpreterDebugFrame? _currentFrame;
    private bool _detached;

    public InterpreterDebugState(
        ISandboxExecutionDebugHook hook,
        SandboxNodeMap nodes,
        SandboxContext context)
    {
        _hook = hook;
        _nodes = nodes;
        _context = context;
    }

    public InterpreterDebugFrame PushFrame(InterpreterFrame frame, FunctionFrameLayout layout)
    {
        var debugFrame = new InterpreterDebugFrame(frame, layout, _context.Budget.Limits, _currentFrame);
        _frames.Add(frame, debugFrame);
        _currentFrame = debugFrame;
        return debugFrame;
    }

    public void PopFrame(InterpreterFrame frame)
    {
        var debugFrame = GetFrame(frame);
        _currentFrame = debugFrame.Caller as InterpreterDebugFrame;
        _frames.Remove(frame);
    }

    public ValueTask CheckpointAsync(
        SandboxDebugCheckpointKind kind,
        SandboxFunction function,
        InterpreterFrame frame,
        SandboxValue? value = null)
        => CheckpointAsync(kind, _nodes.GetDescriptor(function), GetFrame(frame), value, error: null);

    public ValueTask CheckpointAsync(
        SandboxDebugCheckpointKind kind,
        Statement statement,
        InterpreterFrame frame)
        => CheckpointAsync(kind, _nodes.GetDescriptor(statement), GetFrame(frame), value: null, error: null);

    public ValueTask CheckpointAsync(
        SandboxDebugCheckpointKind kind,
        Expression expression,
        InterpreterFrame frame)
        => CheckpointAsync(kind, _nodes.GetDescriptor(expression), GetFrame(frame), value: null, error: null);

    public ValueTask ReportExceptionAsync(SandboxRuntimeException exception)
    {
        if (_reportedException == exception || _currentFrame is null || _currentNode is null)
        {
            return default;
        }

        _reportedException = exception;
        return CheckpointAsync(
            SandboxDebugCheckpointKind.Exception,
            _currentNode,
            _currentFrame,
            value: null,
            exception.Error);
    }

    public SandboxNodeDescriptor? EnterNode(Expression expression)
    {
        var previous = _currentNode;
        _currentNode = _nodes.GetDescriptor(expression);
        return previous;
    }

    public SandboxNodeDescriptor? EnterNode(Statement statement)
    {
        var previous = _currentNode;
        _currentNode = _nodes.GetDescriptor(statement);
        return previous;
    }

    public void RestoreNode(SandboxNodeDescriptor? node) => _currentNode = node;

    private ValueTask CheckpointAsync(
        SandboxDebugCheckpointKind kind,
        SandboxNodeDescriptor node,
        InterpreterDebugFrame frame,
        SandboxValue? value,
        SandboxError? error)
    {
        _currentNode = node;
        if (_detached)
        {
            return default;
        }

        return InvokeHookAsync(new SandboxDebugCheckpoint(_context.RunId, node, kind, frame, value, error));
    }

    private async ValueTask InvokeHookAsync(SandboxDebugCheckpoint checkpoint)
    {
        var suspensionStartedAt = _context.Budget.BeginWallTimeSuspension();
        try
        {
            await _hook.OnCheckpointAsync(checkpoint, _context.CancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!_context.CancellationToken.IsCancellationRequested)
        {
            _detached = true;
        }
        catch (Exception) when (_context.CancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(_context.CancellationToken);
        }
        catch (Exception) when (!_context.CancellationToken.IsCancellationRequested)
        {
            _detached = true;
        }
        finally
        {
            _context.Budget.EndWallTimeSuspension(suspensionStartedAt);
        }
    }

    private InterpreterDebugFrame GetFrame(InterpreterFrame frame)
        => _frames.TryGetValue(frame, out var debugFrame)
            ? debugFrame
            : throw new InvalidOperationException("Interpreter debug frame is not active.");
}
