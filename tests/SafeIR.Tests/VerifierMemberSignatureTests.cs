using System.Reflection;
using System.Reflection.Emit;
using SafeIR.Runtime;
using SafeIR.Verifier;

namespace SafeIR.Tests;

public sealed class VerifierMemberSignatureTests
{
    [Fact]
    public async Task Verifier_rejects_name_only_member_allowlist_entries()
    {
        var policy = VerificationPolicy.BoxedValueDefaults() with {
            AllowedMembers = new HashSet<string>(StringComparer.Ordinal) {
                "SafeIR.Runtime.CompiledRuntime.I32"
            }
        };
        var bytes = VerifierTestHelpers.BuildGeneratedAssembly(type => {
            var method = type.DefineMethod(
                "Execute",
                MethodAttributes.Public | MethodAttributes.Static,
                typeof(SandboxValue),
                [typeof(SandboxContext), typeof(SandboxValue)]);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Call, typeof(CompiledRuntime).GetMethod(nameof(CompiledRuntime.I32))!);
            il.Emit(OpCodes.Ret);
        });

        var result = await VerifierTestHelpers.VerifyAsync(bytes, policy);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Diagnostics, d => d.Code == "V-MEMBER");
    }
}
