namespace DotBoxD.Plugins.Fody;

internal static class DotBoxDInvokeAsyncWeaverNames
{
    public const string GeneratedInterceptorsNamespace = "DotBoxD.Plugins.Generated";
    public const string GeneratedInterceptorsTypeName = "InvokeAsyncInterceptors";
    public const string InvokeAsyncMethodPrefix = "InvokeAsync_";
    public const string ReadCaptureMethodName = "__ReadCapture";
    public const string WriteCaptureMethodName = "__WriteCapture";
    public const string LambdaParameterName = "lambda";
    public const string AsyncStateMachineAttribute = "System.Runtime.CompilerServices.AsyncStateMachineAttribute";
    public const string CompilerGeneratedAttribute = "System.Runtime.CompilerServices.CompilerGeneratedAttribute";
    public const string DelegateTargetGetterName = "get_Target";
    public const string MoveNextMethodName = "MoveNext";

    public const string GeneratedInterceptorsFullName =
        GeneratedInterceptorsNamespace + "." + GeneratedInterceptorsTypeName;
}
