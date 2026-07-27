using DotBoxD.Services.Protocol;

namespace DotBoxD.Services.Benchmarks.Probes;

internal static class ReceivePopulationFrame
{
    public static byte[] Create(int messageId)
    {
        var body = new byte[16];
        for (var index = 0; index < body.Length; index++)
        {
            body[index] = unchecked((byte)(messageId + index));
        }

        using var payload = MessageFramer.FrameToPayload(messageId, MessageType.Response, body);
        return payload.Memory.ToArray();
    }
}
