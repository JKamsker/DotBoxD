namespace DotBoxD.Kernels.Sandbox;

file static class SandboxValueNullGuard
{
    public static T RequireNotNull<T>(T? value, string paramName)
        where T : class
        => value ?? throw new ArgumentNullException(paramName);
}

public sealed record OpaqueIdValue(string TypeName, string Value) : SandboxValue
{
    private const string InvalidTypeName = "<invalid-opaque-id>";

    private string _typeName = SandboxValueNullGuard.RequireNotNull(TypeName, nameof(TypeName));
    private string _value = SandboxValueNullGuard.RequireNotNull(Value, nameof(Value));

    public string TypeName { get => _typeName; init => _typeName = SandboxValueNullGuard.RequireNotNull(value, nameof(value)); }

    public string Value { get => _value; init => _value = SandboxValueNullGuard.RequireNotNull(value, nameof(value)); }

    public override SandboxType Type
        => SandboxType.Scalar(_typeName ?? InvalidTypeName);
}

public sealed record SandboxPath(string RelativePath)
{
    private string _relativePath = SandboxValueNullGuard.RequireNotNull(RelativePath, nameof(RelativePath));

    public string RelativePath { get => _relativePath; init => _relativePath = SandboxValueNullGuard.RequireNotNull(value, nameof(value)); }

    public override string ToString() => RelativePath;
}

public sealed record SandboxPathValue(SandboxPath Value) : SandboxValue
{
    private SandboxPath _value = SandboxValueNullGuard.RequireNotNull(Value, nameof(Value));

    public SandboxPath Value { get => _value; init => _value = SandboxValueNullGuard.RequireNotNull(value, nameof(value)); }

    public override SandboxType Type => SandboxType.SandboxPath;
}

public sealed record SandboxUri(string Value)
{
    private string _value = SandboxValueNullGuard.RequireNotNull(Value, nameof(Value));

    public string Value { get => _value; init => _value = SandboxValueNullGuard.RequireNotNull(value, nameof(value)); }

    public override string ToString() => Value;
}

public sealed record SandboxUriValue(SandboxUri Value) : SandboxValue
{
    private SandboxUri _value = SandboxValueNullGuard.RequireNotNull(Value, nameof(Value));

    public SandboxUri Value { get => _value; init => _value = SandboxValueNullGuard.RequireNotNull(value, nameof(value)); }

    public override SandboxType Type => SandboxType.SandboxUri;
}
