using DotBoxD.Hosting.Execution;
using DotBoxD.Kernels;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Policies;
using DotBoxD.Kernels.Sandbox;

using var host = SandboxHost.Create();
var span = new SourceSpan(1, 1);
var module = new SandboxModule(
    "hello-kernel",
    SemVersion.One,
    SemVersion.One,
    [],
    [new SandboxFunction("main", true, [], SandboxType.I32,
        [new ReturnStatement(new LiteralExpression(SandboxValue.FromInt32(42), span), span)])],
    new Dictionary<string, string>());
var plan = await host.PrepareAsync(module, SandboxPolicyBuilder.Create().WithFuel(100).Build());
var result = await host.ExecuteAsync(plan, "main", SandboxValue.Unit);
Console.WriteLine(result.Succeeded ? ((I32Value)result.Value!).Value : result.Error?.SafeMessage);
