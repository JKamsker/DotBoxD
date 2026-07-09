namespace DotBoxD.Services.Diagnostics;

/// <summary>
/// Describes an internal DotBoxD diagnostic error that was observed but not allowed to
/// interrupt RPC teardown or event dispatch.
/// </summary>
public sealed class RpcDiagnosticErrorEventArgs : EventArgs
{
    public RpcDiagnosticErrorEventArgs(string operation, Exception error)
    {
        if (operation is null)
        {
            throw new ArgumentNullException(nameof(operation));
        }

        if (string.IsNullOrWhiteSpace(operation))
        {
            throw new ArgumentException("Operation must not be empty or whitespace.", nameof(operation));
        }

        Operation = operation;
        Error = error ?? throw new ArgumentNullException(nameof(error));
    }

    /// <summary>The operation that observed the error.</summary>
    public string Operation { get; }

    /// <summary>The observed error.</summary>
    public Exception Error { get; }
}
