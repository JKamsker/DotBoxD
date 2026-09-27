using System.Linq.Expressions;
using DotBoxD.Queryable.Ast;
using DotBoxD.Queryable.Execution;
using DotBoxD.Queryable.Translation;

namespace DotBoxD.Kernels.Tests.Queryable;

public sealed class QueryArrayElementTypeCaptureTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var kind in new[] { "sbyte", "byte", "short", "ushort", "int", "uint", "long", "ulong" })
        {
            foreach (var reinterpreted in new[] { false, true })
            {
                foreach (var method in new[] { "instance", "enumerable", "read-only-span", "span" })
                {
                    yield return [kind, reinterpreted, method];
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Capture_preserves_the_element_type_used_by_Contains(string kind, bool reinterpreted, string method)
    {
        switch (kind)
        {
            case "sbyte":
                AssertMembership<sbyte>(new byte[] { byte.MaxValue }, -1, 0, reinterpreted, method);
                break;
            case "byte":
                AssertMembership<byte>(new sbyte[] { -1 }, byte.MaxValue, 0, reinterpreted, method);
                break;
            case "short":
                AssertMembership<short>(new ushort[] { ushort.MaxValue }, -1, 0, reinterpreted, method);
                break;
            case "ushort":
                AssertMembership<ushort>(new short[] { -1 }, ushort.MaxValue, 0, reinterpreted, method);
                break;
            case "int":
                AssertMembership<int>(new uint[] { uint.MaxValue }, -1, 0, reinterpreted, method);
                break;
            case "uint":
                AssertMembership<uint>(new[] { -1 }, uint.MaxValue, 0, reinterpreted, method);
                break;
            case "long":
                AssertMembership<long>(new ulong[] { ulong.MaxValue }, -1, 0, reinterpreted, method);
                break;
            case "ulong":
                AssertMembership<ulong>(new[] { -1L }, ulong.MaxValue, 0, reinterpreted, method);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    private static void AssertMembership<T>(Array source, T present, T absent, bool reinterpreted, string method)
        where T : IEquatable<T>
    {
        // CLR primitive-array compatibility permits these signed/unsigned views of the same storage.
        T[] values = reinterpreted ? (T[])source : [present];
        Expression<Func<Sample<T>, bool>> predicate = method switch
        {
            "instance" => e => ((ICollection<T>)values).Contains(e.Value),
            "enumerable" => e => Enumerable.Contains(values, e.Value),
            "read-only-span" => e => ((ReadOnlySpan<T>)values).Contains(e.Value),
            "span" => e => ((Span<T>)values).Contains(e.Value),
            _ => throw new ArgumentOutOfRangeException(nameof(method))
        };
        var authored = predicate.Compile();
        Assert.True(authored(new Sample<T>(present)));
        Assert.False(authored(new Sample<T>(absent)));

        var filter = ExpressionQueryTranslator.TranslateFilter(predicate);
        Assert.Equal(QueryFilterKind.In, filter.Kind);
        var reader = new MemberValueReader();
        var compiled = QueryFilterCompiler.Compile(filter, reader);
        foreach (var candidate in new[] { present, absent })
        {
            var value = new Sample<T>(candidate);
            Assert.Equal(authored(value), QueryFilterEvaluator.Evaluate(filter, value, reader));
            Assert.Equal(authored(value), compiled(value));
        }
    }

    private sealed record Sample<T>(T Value);
}
