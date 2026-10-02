using System.Buffers;
using DotBoxD.Codecs.MessagePack;
using DotBoxD.Kernels.Verifier.Generated;
using DotBoxD.Services.Protocol;

namespace DotBoxD.Fuzzing.Targets;

internal static class SeedCorpus
{
    public static void Write(string directory)
    {
        var serializer = new MessagePackRpcSerializer();
        var request = new RpcRequest { MessageId = 42, ServiceName = "Service", MethodName = "Call", InstanceId = "世界🚀" };
        WriteEnvelope(directory, "messagepack-request", "request", serializer, request);
        WriteEnvelope(directory, "messagepack-response", "success", serializer, new RpcResponse { MessageId = -1, IsSuccess = true });
        WriteEnvelope(directory, "messagepack-response", "error", serializer, new RpcResponse
        {
            MessageId = int.MinValue,
            ErrorMessage = "failure",
            ErrorType = "Error.Type"
        });
        request.Streams = [new RpcStreamHandle(43, RpcStreamKind.Binary), new RpcStreamHandle(44, RpcStreamKind.Items)];
        WriteEnvelope(directory, "messagepack-request", "streams", serializer, request);
        WriteEnvelope(directory, "messagepack-response", "stream", serializer, new RpcResponse
        {
            MessageId = 42,
            IsSuccess = true,
            Stream = new RpcStreamHandle(43, RpcStreamKind.Items)
        });
        using var frame = MessageFramer.FrameMessage(serializer, 42, MessageType.Request, request, [0, 1, 255]);
        Write(directory, "framing", "request", frame.Memory.ToArray());
        using var control = MessageFramer.FrameToPayload(42, MessageType.Cancel, []);
        Write(directory, "framing", "cancel", control.Memory.ToArray());
        // A real managed PE reaches metadata/IL verification; a random MZ prefix cannot.
        Write(directory, "verifier", "managed-pe", File.ReadAllBytes(typeof(GeneratedAssemblyVerifier).Assembly.Location));
    }

    private static void WriteEnvelope<T>(string root, string target, string name, MessagePackRpcSerializer serializer, T value)
    {
        var writer = new ArrayBufferWriter<byte>();
        serializer.Serialize(writer, value);
        Write(root, target, name, writer.WrittenSpan.ToArray());
    }

    private static void Write(string root, string target, string name, byte[] bytes)
    {
        var directory = Path.Combine(root, target);
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, name + ".bin"), bytes);
    }
}
