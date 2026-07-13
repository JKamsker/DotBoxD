namespace DotBoxD.Plugins.Analyzer.Analysis.Lowering;

internal static class DotBoxDMetadataNames
{
    public const string PluginAttribute = DotBoxDGenerationNames.TypeNames.PluginAttribute;
    public const string LiveSettingAttribute = DotBoxDGenerationNames.TypeNames.LiveSettingAttribute;
    public const string EventKernelInterface = DotBoxDGenerationNames.TypeNames.EventKernelInterface;
    public const string RangeAttribute = DotBoxDGenerationNames.TypeNames.RangeAttribute;
    public const string HostBindingAttribute = DotBoxDGenerationNames.TypeNames.HostBindingAttribute;
    public const string HostBindingObjectAttribute = DotBoxDGenerationNames.TypeNames.HostBindingObjectAttribute;
    public const string HostBindingIgnoreAttribute = DotBoxDGenerationNames.TypeNames.HostBindingIgnoreAttribute;
    public const string CapabilityAttribute = DotBoxDGenerationNames.TypeNames.CapabilityAttribute;
    public const string KernelMethodAttribute = DotBoxDGenerationNames.TypeNames.KernelMethodAttribute;
    public const string LowerToIrAttribute = DotBoxDGenerationNames.TypeNames.LowerToIrAttribute;
    public const string IRBodyOfAttribute = DotBoxDGenerationNames.TypeNames.IRBodyOfAttribute;
    public const string LowerToIrMethodAttribute = DotBoxDGenerationNames.TypeNames.LowerToIrMethodAttribute;
    public const string PipelineSurfaceAttribute = DotBoxDGenerationNames.TypeNames.PipelineSurfaceAttribute;
    public const string NativeOnlyAttribute = DotBoxDGenerationNames.TypeNames.NativeOnlyAttribute;
    public const string ServerExtensionAttribute = DotBoxDGenerationNames.TypeNames.ServerExtensionAttribute;
    public const string ServerExtensionClientAttribute = DotBoxDGenerationNames.TypeNames.ServerExtensionClientAttribute;
    public const string ServerExtensionMethodAttribute = DotBoxDGenerationNames.TypeNames.ServerExtensionMethodAttribute;
    public const string GeneratePluginServerAttribute = DotBoxDGenerationNames.TypeNames.GeneratePluginServerAttribute;
    public const string GeneratedKernelMethodDescriptorAttribute =
        DotBoxDGenerationNames.TypeNames.GeneratedKernelMethodDescriptorAttribute;
    public const string HookAttribute = DotBoxDHookContractNames.HookAttribute;
    public const string HookResultAttribute = DotBoxDHookContractNames.HookResultAttribute;
    public const string PolymorphicHandleAttribute = DotBoxDGenerationNames.TypeNames.PolymorphicHandleAttribute;
    public const string HandleSubtypeAttribute = DotBoxDGenerationNames.TypeNames.HandleSubtypeAttribute;
    public const string RpcServiceAttribute = DotBoxDGenerationNames.TypeNames.RpcServiceAttribute;
    public const string HookContextType = DotBoxDGenerationNames.TypeNames.HookContext;
    public const string ServerInvocationDelegateType = DotBoxDGenerationNames.TypeNames.ServerInvocationDelegateType;
    public const string ServerInvocationDelegateOriginal = DotBoxDGenerationNames.TypeNames.ServerInvocationDelegateOriginal;
    public const string GameWorldAccessType = DotBoxDGenerationNames.TypeNames.GameWorldAccessType;
    public const string GameWorldMonsterSnapshotType = DotBoxDGenerationNames.TypeNames.GameWorldMonsterSnapshotType;
    public const string ExperimentalAttribute = "System.Diagnostics.CodeAnalysis.ExperimentalAttribute";

    public static bool IsRpcServiceAttribute(string? typeName) =>
        typeName is RpcServiceAttribute;
}
