using DotBoxD.Kernels.Model;

namespace DotBoxD.Kernels.Sandbox.Values;

internal static class SandboxValidatedValueShapeErrors
{
    public static SandboxRuntimeException Quota(string message)
        => new(new SandboxError(SandboxErrorCode.QuotaExceeded, message));

    public static SandboxRuntimeException Error(ValidationFailure failure)
        => new(new SandboxError(failure.Code, failure.Message));
}

internal readonly record struct ValidationFailure(
    SandboxErrorCode Code,
    string? StaticMessage,
    string? BindingId)
{
    public static ValidationFailure Fixed(SandboxErrorCode code, string message)
        => new(code, message, null);

    public static ValidationFailure BindingReturn(string bindingId)
        => new(SandboxErrorCode.BindingFailure, null, bindingId);

    public string Message
        => StaticMessage ?? $"binding '{BindingId}' returned an unexpected value type";
}

internal readonly record struct Frame(SandboxValue Value, SandboxType ExpectedType, int Depth, bool Exit);
