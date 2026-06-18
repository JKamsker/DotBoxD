using BenchmarkDotNet.Running;
using DotBoxD.Services.Benchmarks.Probes;

if (args.Length == 1 && args[0] == "--probe-peer-proxy-cache")
{
    RpcPeerProxyCacheProbe.Run();
    return;
}

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
