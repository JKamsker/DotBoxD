namespace SafeIR.Runtime;

using SafeIR;

public static class SafeFileBindings
{
    public static BindingDescriptor ReadText { get; } = new(
        "file.readText",
        SemVersion.One,
        [SandboxType.SandboxPath],
        SandboxType.String,
        SandboxEffect.Cpu | SandboxEffect.Alloc | SandboxEffect.FileRead,
        "file.read",
        BindingCostModel.PerByte(baseFuel: 50, perByteFuel: 1),
        AuditLevel.PerResource,
        BindingSafety.ReadOnlyExternal,
        async (context, args, cancellationToken) => {
            var text = await SafeFileSystem.ReadTextAsync(context, ((SandboxPathValue)args[0]).Value, cancellationToken)
                .ConfigureAwait(false);
            return SandboxValue.FromString(text);
        },
        CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)));
}
