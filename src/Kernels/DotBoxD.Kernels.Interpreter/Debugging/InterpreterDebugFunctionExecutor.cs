using DotBoxD.Kernels.Debugging;
using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter.Debugging;

using DotBoxD.Kernels;

internal sealed class InterpreterDebugFunctionExecutor
{
    private readonly SandboxContext _context;
    private readonly StatementExecutor _statements;
    private readonly InterpreterDebugState _debug;
    private readonly Func<SandboxFunction, FunctionFrameLayout> _getLayout;

    public InterpreterDebugFunctionExecutor(
        SandboxContext context,
        StatementExecutor statements,
        InterpreterDebugState debug,
        Func<SandboxFunction, FunctionFrameLayout> getLayout)
    {
        _context = context;
        _statements = statements;
        _debug = debug;
        _getLayout = getLayout;
    }

    public async ValueTask<SandboxValue> InvokeAsync(
        SandboxFunction function,
        LocalFunctionArguments arguments,
        SandboxValue? entrypointInput)
    {
        _context.EnterCall();
        InterpreterFrame? frame = null;
        try
        {
            _context.ChargeFuel(1);
            var layout = _getLayout(function);
            frame = entrypointInput is null
                ? InterpreterFrame.Create(layout, function, arguments)
                : InterpreterFrame.CreateValidatedEntrypoint(layout, function, entrypointInput);
            _debug.PushFrame(frame, layout);
            await _debug.CheckpointAsync(
                    SandboxDebugCheckpointKind.FunctionEntry,
                    function,
                    frame)
                .ConfigureAwait(false);

            foreach (var statement in function.Body)
            {
                var result = await _statements.ExecuteStatementAsync(statement, frame).ConfigureAwait(false);
                if (result is null)
                {
                    continue;
                }

                EntrypointBinder.RequireType(result, function.ReturnType, "function return type mismatch");
                await _debug.CheckpointAsync(
                        SandboxDebugCheckpointKind.FunctionExit,
                        function,
                        frame,
                        result)
                    .ConfigureAwait(false);
                return result;
            }

            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.ValidationError,
                $"function '{function.Id}' returned no value"));
        }
        catch (SandboxRuntimeException exception)
        {
            await _debug.ReportExceptionAsync(exception).ConfigureAwait(false);
            throw;
        }
        finally
        {
            if (frame is not null)
            {
                _debug.PopFrame(frame);
            }

            _context.ExitCall();
        }
    }
}
