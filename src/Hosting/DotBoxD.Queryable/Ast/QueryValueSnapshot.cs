using System.Collections.ObjectModel;

namespace DotBoxD.Queryable.Ast;

// Only the factory can create this privately owned read-only snapshot. A public
// ReadOnlyCollection can wrap a mutable list, so its type alone is not sufficient.
internal sealed class QueryValueSnapshot(QueryValue[] values) : ReadOnlyCollection<QueryValue>(values);
