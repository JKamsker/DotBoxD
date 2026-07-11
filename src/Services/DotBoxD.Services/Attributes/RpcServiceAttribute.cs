namespace DotBoxD.Services.Attributes;

/// <summary>
/// Marks an interface as an RPC service. The source generator creates client proxy and server dispatcher
/// implementations for this interface.
/// </summary>
[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public class RpcServiceAttribute : Attribute
{
    private string? _name;

    /// <summary>
    /// Optional custom service name. If not specified, the interface name is used.
    /// </summary>
    public string? Name
    {
        get => _name;
        set
        {
            if (value is not null && string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("RPC service name must not be empty or whitespace.", nameof(Name));
            }

            _name = value;
        }
    }
}
