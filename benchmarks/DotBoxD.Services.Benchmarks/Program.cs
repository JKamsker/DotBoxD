using BenchmarkDotNet.Running;
using DotBoxD.Services.Benchmarks.Probes;

if (args.Length == 1)
{
    switch (args[0])
    {
        case "--probe-peer-proxy-cache":
            RpcPeerProxyCacheProbe.Run();
            return;
        case "--probe-stream-connection-receive-tracking":
            StreamConnectionReceiveTrackingProbe.Run();
            return;
        case "--probe-stream-connection-pending-receive":
            await StreamConnectionPendingReceiveProbe.RunAsync();
            return;
        case "--probe-stream-connection-send-gate":
            StreamConnectionSendGateProbe.Run();
            return;
        case "--probe-generated-metadata-parameters":
            GeneratedMetadataParameterArrayProbe.Run();
            return;
        case "--probe-generated-proxy-default-token":
            GeneratedProxyDefaultTokenProbe.Run();
            return;
        case "--probe-messagepack-envelope-read-state":
            MessagePackEnvelopeReadStateProbe.Run();
            return;
        case "--probe-request-name-cache":
            RpcRequestNameCacheProbe.Run();
            return;
        case "--probe-peer-frame-send":
            RpcPeerFrameSendProbe.Run();
            return;
        case "--probe-peer-start-guard":
            RpcPeerStartGuardProbe.Run();
            return;
        case "--probe-constructor-replay":
            ConstructorReplayGuardProbe.Run();
            return;
        case "--probe-finite-timeout-valuetask-unary":
            await FiniteTimeoutValueTaskUnaryProbe.RunAsync();
            return;
        case "--probe-pending-timeout-scheduler":
            PendingTimeoutSchedulerProbe.Run();
            return;
        case "--probe-frame-read-timeout-source-lifetime":
            FrameReadTimeoutSourceLifetimeProbe.Run();
            return;
    }
}

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
