using DotBoxD.Kernels.Debugging;
using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter.Debugging;

using DotBoxD.Kernels;

internal sealed class InterpreterDebugStatementExecutor
{
    private readonly InterpreterDebugState _debug;
    private readonly SandboxContext _context;
    private readonly Func<Statement, InterpreterFrame, ValueTask<SandboxValue?>> _executeStatement;
    private readonly Func<IReadOnlyList<Statement>, InterpreterFrame, ValueTask<SandboxValue?>> _executeBlock;

    public InterpreterDebugStatementExecutor(
        InterpreterDebugState debug,
        SandboxContext context,
        Func<Statement, InterpreterFrame, ValueTask<SandboxValue?>> executeStatement,
        Func<IReadOnlyList<Statement>, InterpreterFrame, ValueTask<SandboxValue?>> executeBlock)
    {
        _debug = debug;
        _context = context;
        _executeStatement = executeStatement;
        _executeBlock = executeBlock;
    }

    public async ValueTask<SandboxValue?> ExecuteAsync(Statement statement, InterpreterFrame frame)
    {
        var previousNode = _debug.EnterNode(statement);
        try
        {
            await _debug.CheckpointAsync(SandboxDebugCheckpointKind.Statement, statement, frame).ConfigureAwait(false);
            return await _executeStatement(statement, frame).ConfigureAwait(false);
        }
        catch (SandboxRuntimeException exception)
        {
            await _debug.ReportExceptionAsync(exception).ConfigureAwait(false);
            throw;
        }
        finally
        {
            _debug.RestoreNode(previousNode);
        }
    }

    public ValueTask CheckpointLoopIterationAsync(Statement statement, InterpreterFrame frame)
        => _debug.CheckpointAsync(SandboxDebugCheckpointKind.LoopIteration, statement, frame);

    public async ValueTask<SandboxValue?> RunForLoopAsync(
        ForRangeStatement statement,
        int start,
        int end,
        InterpreterFrame frame)
    {
        for (var index = start; index < end; index++)
        {
            _context.ChargeLoopIteration(5);
            frame.WriteInt32(statement.LocalName, index);
            await CheckpointLoopIterationAsync(statement, frame).ConfigureAwait(false);
            var value = await _executeBlock(statement.Body, frame).ConfigureAwait(false);
            if (value is not null && !LoopSignal.IsContinue(value))
            {
                return LoopSignal.IsBreak(value) ? null : value;
            }
        }

        return null;
    }
}
