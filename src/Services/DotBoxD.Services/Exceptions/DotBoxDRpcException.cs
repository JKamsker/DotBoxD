namespace DotBoxD.Services.Exceptions;

/// <summary>
/// Base exception for DotBoxD errors.
/// </summary>
public class DotBoxDRpcException : Exception
{
    public DotBoxDRpcException()
    {
    }

    public DotBoxDRpcException(string message) : base(message)
    {
    }

    public DotBoxDRpcException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when a remote RPC call fails.
/// </summary>
public class DotBoxDRpcRemoteException : DotBoxDRpcException
{
    /// <summary>
    /// The type name of the remote exception.
    /// </summary>
    public string RemoteExceptionType { get; }

    public DotBoxDRpcRemoteException(string message, string remoteExceptionType)
        : base(message)
    {
        RemoteExceptionType = remoteExceptionType;
    }
}

/// <summary>
/// Exception thrown when a connection fails.
/// </summary>
public class DotBoxDRpcConnectionException : DotBoxDRpcException
{
    public DotBoxDRpcConnectionException(string message) : base(message)
    {
    }

    public DotBoxDRpcConnectionException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when a request times out.
/// </summary>
public class DotBoxDRpcTimeoutException : DotBoxDRpcException
{
    public DotBoxDRpcTimeoutException(string message) : base(message)
    {
    }
}

/// <summary>
/// Exception thrown when a service, method, or sub-service instance is not found.
/// </summary>
public class DotBoxDRpcNotFoundException : DotBoxDRpcException
{
    /// <summary>Distinguishes which lookup produced the not-found result.</summary>
    public enum NotFoundKind
    {
        /// <summary>No service is registered under the requested name.</summary>
        Service,

        /// <summary>The service exists but exposes no method with the requested name.</summary>
        Method,

        /// <summary>The sub-service instance id is unknown or has expired.</summary>
        Instance,
    }

    public DotBoxDRpcNotFoundException(string message) : this(message, NotFoundKind.Service)
    {
    }

    public DotBoxDRpcNotFoundException(string message, NotFoundKind kind) : base(message)
    {
        Kind = kind;
    }

    /// <summary>Which lookup produced this not-found result.</summary>
    public NotFoundKind Kind { get; }
}

/// <summary>
/// Exception thrown when an inbound DotBoxD frame is malformed or cannot be decoded.
/// </summary>
public class DotBoxDRpcProtocolException : DotBoxDRpcException
{
    public DotBoxDRpcProtocolException(string message) : base(message)
    {
    }
}
