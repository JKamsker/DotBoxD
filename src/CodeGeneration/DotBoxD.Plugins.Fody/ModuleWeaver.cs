using Fody;

namespace DotBoxD.Plugins.Fody;

public sealed class ModuleWeaver : BaseModuleWeaver
{
    public override void Execute()
    {
        var result = InvokeAsyncClosureCaptureWeaver.Rewrite(
            ModuleDefinition,
            WriteInfo,
            WriteWarning);

        if (result.RewrittenAccesses > 0)
        {
            WriteInfo(
                $"DotBoxD InvokeAsync closure weaver rewrote {result.RewrittenAccesses} capture access(es) " +
                $"in {result.RewrittenInterceptors} interceptor(s).");
        }
    }

    public override IEnumerable<string> GetAssembliesForScanning()
    {
        yield return "mscorlib";
        yield return "netstandard";
        yield return "System.Private.CoreLib";
    }

    public override bool ShouldCleanReference => false;
}
