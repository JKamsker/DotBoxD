using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Runtime;
using DotBoxD.Kernels.Sandbox;
using DotBoxD.Kernels.Serialization.Json.Hosting;
using DotBoxD.Kernels.Tests._TestSupport;
using DotBoxD.Kernels.Tests.Interpreter;

namespace DotBoxD.Kernels.Tests.Policy;

internal static class PolicyMutationTestSupport
{
    public static SandboxHost CreateDefaultHost() => SandboxTestHost.Create();

    public static async Task<SandboxValidationException> PrepareThrowsAsync(
        SandboxModule module,
        SandboxPolicy policy)
        => await PrepareThrowsAsync(CreateDefaultHost(), module, policy);

    public static async Task<SandboxValidationException> PrepareThrowsAsync(
        SandboxHost host,
        SandboxModule module,
        SandboxPolicy policy)
        => await Assert.ThrowsAsync<SandboxValidationException>(async () =>
            await host.PrepareAsync(module, policy));

    public static async Task<SandboxModule> PureModuleAsync()
        => await CreateDefaultHost().ImportJsonAsync(SandboxTestHost.PureScoreJson("policy-mutation-pure"));

    public static async Task<SandboxModule> FileReadModuleAsync()
        => await CreateDefaultHost().ImportJsonAsync(InterpreterAndPolicyTests.FileReadJson("settings.json"));

    public static async Task<SandboxModule> FileWriteModuleAsync()
        => await CreateDefaultHost().ImportJsonAsync(FileWriteJson("out.txt", "x"));

    public static async Task<SandboxModule> TimeModuleAsync()
        => await CreateDefaultHost().ImportJsonAsync(TimeJson());

    public static async Task<SandboxModule> RandomModuleAsync()
        => await CreateDefaultHost().ImportJsonAsync(RandomJson());

    public static async Task<SandboxModule> CustomBindingModuleAsync()
        => await CustomCapabilityHost().ImportJsonAsync(CustomBindingJson());

    public static SandboxHost CustomCapabilityHost()
        => SandboxHost.Create(builder =>
        {
            builder.AddDefaultPureBindings();
            builder.AddBinding(CustomBinding());
            builder.UseInterpreter();
        });

    public static SandboxModule EventRequestModule(string capability)
        => new(
            "event-request-policy-mutation",
            SemVersion.One,
            SandboxLanguage.CurrentVersion,
            [new CapabilityRequest(capability, "test event")],
            [
                new SandboxFunction(
                    "main",
                    IsEntrypoint: true,
                    [],
                    SandboxType.Unit,
                    [
                        new ReturnStatement(
                            new LiteralExpression(SandboxValue.Unit, new SourceSpan(0, 0)),
                            new SourceSpan(0, 0))
                    ])
            ],
            new Dictionary<string, string>());

    public static Dictionary<string, string> FileReadParameters(string root)
        => new()
        {
            ["root"] = root,
            ["maxBytesPerRun"] = "1024"
        };

    public static Dictionary<string, string> FileWriteParameters(string root)
        => new()
        {
            ["root"] = root,
            ["maxBytesPerRun"] = "1024",
            ["allowCreate"] = "true",
            ["allowOverwrite"] = "false"
        };

    public static SandboxPolicy PurePolicy(string id)
        => new(id, SandboxEffects.Pure, [], new ResourceLimits(MaxFuel: 1_000));

    public static void AssertDiagnostic(
        SandboxValidationException ex,
        string code,
        string messagePart)
        => Assert.Contains(ex.Diagnostics, d =>
            d.Code == code && d.Message.Contains(messagePart, StringComparison.Ordinal));

    private static BindingDescriptor CustomBinding()
        => new(
            "probe.read",
            SemVersion.One,
            [],
            SandboxType.I32,
            SandboxEffects.Pure | SandboxEffect.HostStateRead | SandboxEffect.Audit,
            "probe.read",
            BindingCostModel.Fixed(1),
            AuditLevel.PerCall,
            BindingSafety.ReadOnlyExternal,
            static (_, _, _) => ValueTask.FromResult<SandboxValue>(SandboxValue.FromInt32(1)),
            CompiledBinding.RuntimeStub(typeof(CompiledRuntime).FullName!, nameof(CompiledRuntime.CallBinding)),
            static (grant, diagnostics) =>
            {
                if (!grant.Parameters.TryGetValue("scope", out var scope) ||
                    string.IsNullOrWhiteSpace(scope))
                {
                    diagnostics.Add(new SandboxDiagnostic("E-PROBE-GRANT", "scope is required"));
                }
            });

    private static string FileWriteJson(string path, string text)
        => $$"""
        {
          "id": "policy-mutation-file-writer",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "file.write" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "Unit",
              "body": [
                {
                  "op": "return",
                  "value": {
                    "call": "file.writeText",
                    "args": [{ "path": "{{path}}" }, { "string": "{{text}}" }]
                  }
                }
              ]
            }
          ]
        }
        """;

    private static string TimeJson()
        => """
        {
          "id": "policy-mutation-time",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "time.now" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I64",
              "body": [{ "op": "return", "value": { "call": "time.nowUnixMillis", "args": [] } }]
            }
          ]
        }
        """;

    private static string RandomJson()
        => """
        {
          "id": "policy-mutation-random",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "random" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [
                {
                  "op": "return",
                  "value": { "call": "random.nextI32", "args": [{ "i32": 0 }, { "i32": 10 }] }
                }
              ]
            }
          ]
        }
        """;

    private static string CustomBindingJson()
        => """
        {
          "id": "policy-mutation-custom-binding",
          "version": "1.0.0",
          "capabilityRequests": [{ "id": "probe.read" }],
          "functions": [
            {
              "id": "main",
              "visibility": "entrypoint",
              "parameters": [],
              "returnType": "I32",
              "body": [{ "op": "return", "value": { "call": "probe.read", "args": [] } }]
            }
          ]
        }
        """;

    public sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path) => Path = path;

        public string Path { get; }

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "dotboxd-policy-mutation-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
