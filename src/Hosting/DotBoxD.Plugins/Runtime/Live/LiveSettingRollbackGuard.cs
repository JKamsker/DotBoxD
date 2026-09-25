namespace DotBoxD.Plugins.Runtime;

internal sealed class LiveSettingRollbackGuard
{
    private bool _isActive;

    public void ThrowIfActive()
    {
        if (_isActive)
        {
            throw new InvalidOperationException("Live settings cannot be updated during rollback.");
        }
    }

    public void Execute(Action rollback, Exception updateFailure)
    {
        try
        {
            _isActive = true;
            rollback();
        }
        catch (Exception rollbackFailure)
        {
            throw new AggregateException(
                "Live setting update failed and rollback did not complete.",
                updateFailure,
                rollbackFailure);
        }
        finally
        {
            _isActive = false;
        }
    }
}
