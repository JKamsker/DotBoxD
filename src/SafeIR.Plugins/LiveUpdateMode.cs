namespace SafeIR.Plugins;

[Flags]
public enum LiveUpdateMode
{
    Sync = 0,
    AsyncSet = 1,
    AsyncGet = 2,
    FullAsync = AsyncSet | AsyncGet
}
