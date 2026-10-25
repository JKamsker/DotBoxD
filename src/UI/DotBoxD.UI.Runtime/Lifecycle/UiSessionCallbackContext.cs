namespace DotBoxD.UI.Runtime;

internal sealed class UiSessionCallbackContext
{
    private readonly AsyncLocal<bool> _active = new();
    public bool IsActive => _active.Value;

    public IDisposable Enter()
    {
        var scope = new Scope(this, _active.Value);
        _active.Value = true;
        return scope;
    }

    private sealed class Scope(UiSessionCallbackContext context, bool previous) : IDisposable
    {
        private UiSessionCallbackContext? _context = context;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _context, null) is { } owner)
            { owner._active.Value = previous; }
        }
    }
}
