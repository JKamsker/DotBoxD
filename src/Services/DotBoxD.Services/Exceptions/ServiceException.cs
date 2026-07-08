namespace DotBoxD.Services.Exceptions;

/// <summary>
/// Base exception for DotBoxD errors.
/// </summary>
public class ServiceException : Exception
{
    public ServiceException()
    {
    }

    public ServiceException(string message) : base(message)
    {
    }

    public ServiceException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when a remote RPC call fails.
/// </summary>
public class RemoteServiceException : ServiceException
{
    /// <summary>
    /// The type name of the remote exception.
    /// </summary>
    public string RemoteExceptionType { get; }

    public RemoteServiceException(string message, string remoteExceptionType)
        : base(message)
    {
        RemoteExceptionType = remoteExceptionType;
    }
}

/// <summary>
/// Exception thrown when a connection fails.
/// </summary>
public class ServiceConnectionException : ServiceException
{
    public ServiceConnectionException(string message) : base(message)
    {
    }

    public ServiceConnectionException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when a request times out.
/// </summary>
public class ServiceTimeoutException : ServiceException
{
    public ServiceTimeoutException(string message) : base(message)
    {
    }
}

/// <summary>
/// Exception thrown when a service, method, or sub-service instance is not found.
/// </summary>
public class ServiceNotFoundException : ServiceException
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

    public ServiceNotFoundException(string message) : this(message, NotFoundKind.Service)
    {
    }

    public ServiceNotFoundException(string message, NotFoundKind kind) : base(message)
    {
        Kind = ValidateKind(kind);
    }

    /// <summary>Which lookup produced this not-found result.</summary>
    public NotFoundKind Kind { get; }

    private static NotFoundKind ValidateKind(NotFoundKind kind) =>
        kind switch
        {
            NotFoundKind.Service or NotFoundKind.Method or NotFoundKind.Instance => kind,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown service not-found kind."),
        };
}

/// <summary>
/// Exception thrown when an inbound DotBoxD frame is malformed or cannot be decoded.
/// </summary>
public class ServiceProtocolException : ServiceException
{
    public ServiceProtocolException(string message) : base(message)
    {
    }
}
