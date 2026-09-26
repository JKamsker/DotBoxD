using System.Linq.Expressions;
using System.Runtime.CompilerServices;

namespace DotBoxD.Queryable.Translation;

internal sealed class LinqMembershipValues<T>(MethodCallExpression call)
{
    private readonly HashSet<object> _active = new(ReferenceEqualityComparer.Instance);

    public IEnumerable<T> Read(IEnumerable<T> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        RuntimeHelpers.EnsureSufficientExecutionStack();
        if (!_active.Add(source))
        {
            throw QueryTranslationException.Unsupported(call, "cyclic Contains sources cannot define portable membership; use ToArray().");
        }

        try
        {
            IEnumerable<T> values;
            if (source is ICollection<T>)
            {
                CollectionContainsSupport.Validate(call, source);
                ContainsMethodFilterTranslator.RejectUnsupportedContainsComparer(call, source);
                values = source;
            }
            else if (LinqIteratorReader.ContainsOwner(source) is { } owner)
            {
                values = ReadIterator(source, owner.Name);
            }
            else
            {
                values = source;
            }

            foreach (var item in values)
            {
                yield return item;
            }
        }
        finally
        {
            _active.Remove(source);
        }
    }

    private IEnumerable<T> ReadIterator(IEnumerable<T> iterator, string owner) =>
        LinqIteratorReader.MembershipKind(owner) switch
        {
            IteratorMembership.Enumeration => iterator,
            IteratorMembership.Source => Read(Source(iterator)),
            IteratorMembership.Concatenation => LinqIteratorReader.Sources<T>(iterator).SelectMany(Read),
            IteratorMembership.Set => ReadSet(iterator, owner),
            IteratorMembership.Appended => ReadAppended(iterator, owner),
            IteratorMembership.Default => ReadDefault(iterator),
            // Select each inner source once, then validate and capture that same instance.
            IteratorMembership.Selected => LinqIteratorReader.SelectedSources<T>(iterator).SelectMany(Read),
            IteratorMembership.ShuffleTake => ReadShuffleTake(iterator),
            _ => throw QueryTranslationException.Unsupported(call,
                "this framework iterator's Contains dispatch is not supported; use ToArray() for enumeration membership.")
        };

    private IEnumerable<T> ReadSet(IEnumerable<T> iterator, string owner)
    {
        if (LinqIteratorReader.Field(iterator, "_comparer") is not null)
        {
            return iterator;
        }

        var values = owner == "DistinctIterator`1"
            ? Read(Source(iterator)) : LinqIteratorReader.Sources<T>(iterator).SelectMany(Read);
        return values.Distinct();
    }

    private IEnumerable<T> ReadAppended(IEnumerable<T> iterator, string owner)
    {
        if (owner == "AppendPrepend1Iterator`1")
        {
            var item = (T)LinqIteratorReader.Field(iterator, "_item")!;
            var source = Read(Source(iterator));
            return (bool)LinqIteratorReader.Field(iterator, "_appending")! ? source.Append(item) : source.Prepend(item);
        }

        return LinqIteratorReader.LinkedValues<T>(LinqIteratorReader.Field(iterator, "_prepended"))
            .Concat(Read(Source(iterator)))
            .Concat(LinqIteratorReader.LinkedValues<T>(LinqIteratorReader.Field(iterator, "_appended")).Reverse());
    }

    private IEnumerable<T> ReadDefault(IEnumerable<T> iterator)
    {
        var inner = Source(iterator);
        return inner.TryGetNonEnumeratedCount(out var count)
            ? count > 0 ? Read(inner) : [(T)LinqIteratorReader.Field(iterator, "_default")!]
            : iterator;
    }

    private IEnumerable<T> ReadShuffleTake(IEnumerable<T> iterator)
    {
        var source = Source(iterator);
        var take = (int)LinqIteratorReader.Field(iterator, "_takeCount")!;
        // The runtime forwards only these two full-source cases. HashSet, for example, is
        // enumerated with default equality even when the take count exceeds its size.
        return (source is IList<T> list && list.Count <= take) ||
            (source is not IList<T> && LinqIteratorReader.ContainsOwner(source) is not null &&
                source.TryGetNonEnumeratedCount(out var count) && count <= take)
            ? Read(source) : iterator;
    }

    private static IEnumerable<T> Source(object iterator) => (IEnumerable<T>)LinqIteratorReader.Field(iterator, "_source")!;
}
