namespace DotBoxD.Abstractions;

/// <summary>
/// Public carrier for a generated anonymous server invocation. The generated plugin facade consumes the
/// package factory as its concrete plugin package type, while abstractions remain independent of the plugin
/// hosting assembly.
/// </summary>
public sealed class IRInvocation<TDelegate, TReturn>
    where TDelegate : Delegate
{
    private IRInvocation(
        string pluginId,
        Func<object> packageFactory,
        Func<TDelegate, byte[]> encodeArguments,
        Func<TDelegate, ReadOnlyMemory<byte>, TReturn> decodeResult)
    {
        PluginId = string.IsNullOrWhiteSpace(pluginId)
            ? throw new ArgumentException("Plugin id is required.", nameof(pluginId))
            : pluginId;
        PackageFactory = packageFactory ?? throw new ArgumentNullException(nameof(packageFactory));
        EncodeArguments = encodeArguments ?? throw new ArgumentNullException(nameof(encodeArguments));
        DecodeResult = decodeResult ?? throw new ArgumentNullException(nameof(decodeResult));
    }

    public string PluginId { get; }

    public Func<object> PackageFactory { get; }

    public Func<TDelegate, byte[]> EncodeArguments { get; }

    public Func<TDelegate, ReadOnlyMemory<byte>, TReturn> DecodeResult { get; }

    public static IRInvocation<TDelegate, TReturn> FromGenerated(
        string pluginId,
        Func<object> packageFactory,
        Func<TDelegate, byte[]> encodeArguments,
        Func<TDelegate, ReadOnlyMemory<byte>, TReturn> decodeResult)
        => new(pluginId, packageFactory, encodeArguments, decodeResult);
}

/// <summary>
/// Public carrier for a generated anonymous server invocation whose delegate is paired with an explicit
/// capture bag.
/// </summary>
public sealed class IRInvocation<TCaptures, TDelegate, TReturn>
    where TCaptures : class
    where TDelegate : Delegate
{
    private IRInvocation(
        string pluginId,
        Func<object> packageFactory,
        Func<TCaptures, TDelegate, byte[]> encodeArguments,
        Func<TCaptures, TDelegate, ReadOnlyMemory<byte>, TReturn> decodeResult)
    {
        PluginId = string.IsNullOrWhiteSpace(pluginId)
            ? throw new ArgumentException("Plugin id is required.", nameof(pluginId))
            : pluginId;
        PackageFactory = packageFactory ?? throw new ArgumentNullException(nameof(packageFactory));
        EncodeArguments = encodeArguments ?? throw new ArgumentNullException(nameof(encodeArguments));
        DecodeResult = decodeResult ?? throw new ArgumentNullException(nameof(decodeResult));
    }

    public string PluginId { get; }

    public Func<object> PackageFactory { get; }

    public Func<TCaptures, TDelegate, byte[]> EncodeArguments { get; }

    public Func<TCaptures, TDelegate, ReadOnlyMemory<byte>, TReturn> DecodeResult { get; }

    public static IRInvocation<TCaptures, TDelegate, TReturn> FromGenerated(
        string pluginId,
        Func<object> packageFactory,
        Func<TCaptures, TDelegate, byte[]> encodeArguments,
        Func<TCaptures, TDelegate, ReadOnlyMemory<byte>, TReturn> decodeResult)
        => new(pluginId, packageFactory, encodeArguments, decodeResult);
}
