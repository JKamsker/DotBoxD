using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Interpreter.Frame;

internal readonly struct LocalFunctionArguments
{
    private readonly SandboxValue[]? _array;
    private readonly SandboxValue? _first;
    private readonly SandboxValue? _second;
    private readonly int _scalarCount;

    private LocalFunctionArguments(
        SandboxValue[]? array,
        SandboxValue? first,
        SandboxValue? second,
        int scalarCount)
    {
        _array = array;
        _first = first;
        _second = second;
        _scalarCount = scalarCount;
    }

    public SandboxValue this[int index]
    {
        get
        {
            if (_array is not null)
            {
                return _array[index];
            }

            return index switch
            {
                0 when _scalarCount >= 1 => _first!,
                1 when _scalarCount == 2 => _second!,
                _ => throw new IndexOutOfRangeException()
            };
        }
    }

    public static LocalFunctionArguments FromArray(SandboxValue[] arguments)
        => new(arguments, null, null, scalarCount: 0);

    public static LocalFunctionArguments FromSingle(SandboxValue argument)
        => new(null, argument, null, scalarCount: 1);

    public static LocalFunctionArguments FromPair(SandboxValue first, SandboxValue second)
        => new(null, first, second, scalarCount: 2);
}

internal readonly struct LocalFunctionTripleArguments
{
    private readonly SandboxValue _first;
    private readonly SandboxValue _second;
    private readonly SandboxValue _third;

    public LocalFunctionTripleArguments(
        SandboxValue first,
        SandboxValue second,
        SandboxValue third)
    {
        _first = first;
        _second = second;
        _third = third;
    }

    public SandboxValue this[int index]
        => index switch
        {
            0 => _first,
            1 => _second,
            2 => _third,
            _ => throw new IndexOutOfRangeException()
        };
}
