using System.Collections;
using System.Collections.ObjectModel;
using DotBoxD.Kernels.Bindings;

namespace DotBoxD.Kernels.Tests.Audit;

internal static class AuditFieldSnapshotFixture
{
    public static Dictionary<string, string> Fields()
        => new(StringComparer.Ordinal) { ["Alpha"] = "one", ["Beta"] = "two" };

    public static IReadOnlyDictionary<string, string>? Input(string kind, Dictionary<string, string> fields)
        => kind switch
        {
            "dictionary" => fields,
            "read-only" => new ReadOnlyDictionary<string, string>(fields),
            "enumerable" => new ObservedDictionary(fields),
            "case-insensitive" => new Dictionary<string, string>(fields, StringComparer.OrdinalIgnoreCase),
            "empty" => new Dictionary<string, string>(StringComparer.Ordinal),
            "null" => null,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    public static SandboxAuditEvent Event(IReadOnlyDictionary<string, string>? fields)
        => new(SandboxRunId.New(), "event", DateTimeOffset.UnixEpoch, true, Fields: fields);

    internal sealed class ObservedDictionary(Dictionary<string, string> fields, Exception? failure = null)
        : IReadOnlyDictionary<string, string>
    {
        public int Enumerations { get; private set; }
        public int Count => fields.Count;
        public IEnumerable<string> Keys => fields.Keys;
        public IEnumerable<string> Values => fields.Values;
        public string this[string key] => fields[key];
        public bool ContainsKey(string key) => fields.ContainsKey(key);
        public bool TryGetValue(string key, out string value) => fields.TryGetValue(key, out value!);

        public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
        {
            Enumerations++;
            return failure is null ? fields.GetEnumerator() : EnumerateUntilFailure();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private IEnumerator<KeyValuePair<string, string>> EnumerateUntilFailure()
        {
            yield return new KeyValuePair<string, string>("Alpha", fields["Alpha"]);
            throw failure!;
        }
    }
}
