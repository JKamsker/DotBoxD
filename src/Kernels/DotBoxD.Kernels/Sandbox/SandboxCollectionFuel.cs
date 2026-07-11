using DotBoxD.Kernels.Model;

namespace DotBoxD.Kernels.Sandbox;

public static class SandboxCollectionFuel
{
    private const long BaseCost = 2;
    private static readonly HashSet<string> CollectionIntrinsics = new(StringComparer.Ordinal)
    {
        "list.empty",
        "list.of",
        "list.count",
        "list.get",
        "list.add",
        "map.empty",
        "map.containsKey",
        "map.get",
        "map.set",
        "map.remove"
    };

    private static readonly HashSet<string> EmptyCalls = new(StringComparer.Ordinal)
    {
        "list.empty",
        "map.empty"
    };

    private static readonly HashSet<string> ReadCalls = new(StringComparer.Ordinal)
    {
        "list.count",
        "list.get",
        "map.containsKey",
        "map.get"
    };

    private static readonly HashSet<string> AddCalls = new(StringComparer.Ordinal)
    {
        "list.add",
        "map.set"
    };

    public static bool IsCollectionIntrinsic(string callName)
        => CollectionIntrinsics.Contains(callName);

    public static long Empty() => BaseCost;

    public static long Read(int count = 0) => BaseCost + Math.Max(0, count);

    public static long Copy(int sourceCount, int addedCount = 0)
        => BaseCost + Math.Max(0, sourceCount) + Math.Max(0, addedCount);

    internal static long AllocationBytes(int elementCount, int bytesPerElement, bool minimumOne = false)
        => AllocationBytes((long)Math.Max(0, elementCount), bytesPerElement, minimumOne);

    internal static long AllocationBytes(
        int sourceCount,
        int addedCount,
        int bytesPerElement,
        bool minimumOne = false)
        => AllocationBytes(
            (long)Math.Max(0, sourceCount) + Math.Max(0, addedCount),
            bytesPerElement,
            minimumOne);

    public static long EstimateCall(string callName, int argumentCount)
    {
        if (EmptyCalls.Contains(callName))
        {
            return Empty();
        }

        if (string.Equals(callName, "list.of", StringComparison.Ordinal))
        {
            return Copy(argumentCount);
        }

        if (ReadCalls.Contains(callName))
        {
            return Read();
        }

        if (AddCalls.Contains(callName))
        {
            return Copy(argumentCount, addedCount: 1);
        }

        return string.Equals(callName, "map.remove", StringComparison.Ordinal) ? Copy(argumentCount) : 0;
    }

    private static long AllocationBytes(long elementCount, int bytesPerElement, bool minimumOne)
    {
        var chargedElements = minimumOne ? Math.Max(1L, elementCount) : elementCount;
        try
        {
            return checked(chargedElements * bytesPerElement);
        }
        catch (OverflowException)
        {
            throw new SandboxRuntimeException(new SandboxError(
                SandboxErrorCode.QuotaExceeded,
                "collection copy allocation budget exhausted"));
        }
    }
}
