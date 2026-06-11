namespace SafeIR.Hosting;

using SafeIR;
using SafeIR.Compiler;
using SafeIR.Interpreter;
using SafeIR.Runtime;
using SafeIR.Verifier;

public sealed class SandboxHostBuilder
{
    private readonly BindingRegistryBuilder _bindings = new();
    private ISandboxInterpreter? _interpreter;
    private ISandboxCompiler? _compiler;

    public SandboxHostBuilder AddDefaultPureBindings()
    {
        _bindings.AddDefaultPureBindings();
        return this;
    }

    public SandboxHostBuilder AddFileBindings()
    {
        _bindings.AddFileBindings();
        return this;
    }

    public SandboxHostBuilder AddBinding(BindingDescriptor descriptor)
    {
        _bindings.Add(descriptor);
        return this;
    }

    public SandboxHostBuilder UseInterpreter(ISandboxInterpreter? interpreter = null)
    {
        _interpreter = interpreter ?? new SandboxInterpreter();
        return this;
    }

    public SandboxHostBuilder UseCompilerIfAvailable(ISandboxCompiler? compiler = null)
    {
        _compiler = compiler ?? new ReflectionEmitSandboxCompiler(new GeneratedAssemblyVerifier());
        return this;
    }

    internal SandboxHost Build()
    {
        _interpreter ??= new SandboxInterpreter();
        return new SandboxHost(_bindings.Build(), _interpreter, _compiler);
    }
}
