using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Sandbox.Values;

namespace DotBoxD.Kernels.Bindings;

internal static class BindingReturnCost
{
    public static long MeasureBytes(SandboxValue value)
        => SandboxValueShapeMeter.Measure(value).StringBytes;

    public static long MeasureBytes(ValueShape shape) => shape.StringBytes;
}
