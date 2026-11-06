using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using DotBoxD.Kernels.Model;

namespace DotBoxD.Kernels.Serialization.Json.Internal;

internal sealed class JsonSourceMap
{
    private readonly JsonElement _root;
    private readonly Dictionary<long, SourceSpan> _spansByOffset;

    private JsonSourceMap(JsonElement root, Dictionary<long, SourceSpan> spansByOffset)
    {
        _root = root;
        _spansByOffset = spansByOffset;
    }

    public static JsonSourceMap Create(string json, JsonElement root)
    {
        var tokenSpans = JsonTokenSpans.Read(json);
        var map = new JsonSourceMap(root, new Dictionary<long, SourceSpan>(tokenSpans.Count));
        var index = 0;
        map.Visit(root, tokenSpans, ref index);
        return map;
    }

    public SourceSpan SpanFor(JsonElement element)
        => TryGetOffset(element, out var offset) && _spansByOffset.TryGetValue(offset, out var span)
            ? span
            : JsonImport.JsonSpan;

    private void Visit(JsonElement element, IReadOnlyList<SourceSpan> tokenSpans, ref int index)
    {
        var span = index < tokenSpans.Count ? tokenSpans[index] : JsonImport.JsonSpan;
        index++;
        if (TryGetOffset(element, out var offset))
        {
            _spansByOffset[offset] = span;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Visit(property.Value, tokenSpans, ref index);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Visit(item, tokenSpans, ref index);
                }

                break;
        }
    }

    /// <summary>
    /// Identifies an element by where its raw UTF-8 value starts inside the root's raw value. Every
    /// value token starts at a distinct byte, so the offset is a unique, O(1) identity for elements of
    /// this tree. (Keying on <see cref="JsonElement"/> itself falls back to reflection-based equality and
    /// shape hashes that collide for identically shaped containers, which made large payloads quadratic.)
    /// Elements outside the root's byte range belong to another tree and have no span here.
    /// </summary>
    private bool TryGetOffset(JsonElement element, out long offset)
    {
        offset = 0;
        if (element.ValueKind == JsonValueKind.Undefined)
        {
            return false;
        }

        var root = JsonMarshal.GetRawUtf8Value(_root);
        var raw = JsonMarshal.GetRawUtf8Value(element);
        offset = Unsafe.ByteOffset(ref MemoryMarshal.GetReference(root), ref MemoryMarshal.GetReference(raw));
        return offset >= 0 && offset + raw.Length <= root.Length;
    }

    private static class JsonTokenSpans
    {
        public static IReadOnlyList<SourceSpan> Read(string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 64
            });
            var positions = new SourcePositionTracker(json);
            var spans = new List<SourceSpan>();
            while (reader.Read())
            {
                if (IsValueToken(reader.TokenType))
                {
                    spans.Add(positions.SpanAt(reader.TokenStartIndex));
                }
            }

            return spans;
        }

        private static bool IsValueToken(JsonTokenType tokenType)
            => tokenType is JsonTokenType.StartObject
                or JsonTokenType.StartArray
                or JsonTokenType.String
                or JsonTokenType.Number
                or JsonTokenType.True
                or JsonTokenType.False
                or JsonTokenType.Null;
    }

    private sealed class SourcePositionTracker
    {
        private readonly string _json;
        private int _charIndex;
        private long _byteOffset;
        private int _line = 1;
        private int _column = 1;

        public SourcePositionTracker(string json)
            => _json = json;

        public SourceSpan SpanAt(long byteOffset)
        {
            while (_charIndex < _json.Length && _byteOffset < byteOffset)
            {
                Advance();
            }

            return new SourceSpan(_line, _column);
        }

        private void Advance()
        {
            var current = _json[_charIndex];
            if (current == '\r')
            {
                AdvanceNewLine(charCount: IsCrLf() ? 2 : 1);
                return;
            }

            if (current == '\n')
            {
                AdvanceNewLine(charCount: 1);
                return;
            }

            var charCount = IsSurrogatePair() ? 2 : 1;
            _byteOffset += Encoding.UTF8.GetByteCount(_json.AsSpan(_charIndex, charCount));
            _charIndex += charCount;
            _column += charCount;
        }

        private void AdvanceNewLine(int charCount)
        {
            _byteOffset += charCount;
            _charIndex += charCount;
            _line++;
            _column = 1;
        }

        private bool IsCrLf()
            => _charIndex + 1 < _json.Length && _json[_charIndex + 1] == '\n';

        private bool IsSurrogatePair()
            => char.IsHighSurrogate(_json[_charIndex]) &&
                _charIndex + 1 < _json.Length &&
                char.IsLowSurrogate(_json[_charIndex + 1]);
    }
}
