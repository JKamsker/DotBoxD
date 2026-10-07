using System.Linq.Expressions;
using System.Reflection;

namespace DotBoxD.Queryable.Execution;

internal static class QueryMemberCompiler
{
    public static Expression? TryBuild(MemberValueReader reader, string path, ParameterExpression target,
        Func<Expression, Expression> compare, Expression fallback)
    {
        var root = reader.DeclaredRootType;
        if (root is null || Nullable.GetUnderlyingType(root) is not null)
        {
            return null;
        }

        MemberInfo[] chain;
        try
        {
            chain = MemberValueReader.ResolveChain(root, path);
        }
        catch (InvalidOperationException)
        {
            // Keep unresolved/ambiguous paths lazy, as in the original compiled reader call.
            return null;
        }

        var variables = new List<ParameterExpression>();
        var expressions = new List<Expression>();
        var done = Expression.Label(typeof(bool), "done");
        var nullResult = compare(Expression.Constant(null, typeof(object)));
        // Unbox by address so a mutating struct getter observes the same boxed receiver
        // as reflection. Convert would call the getter on a temporary value copy.
        Expression current = root.IsValueType ? Expression.Unbox(target, root) : Expression.Convert(target, root);
        foreach (var member in chain)
        {
            // Reflection boxes nullable intermediates before the next read. Keep that uncommon
            // path (and non-boxable/by-ref results) on the reader to preserve its exact behavior.
            if (Nullable.GetUnderlyingType(current.Type) is not null)
            {
                return null;
            }

            if (!current.Type.IsValueType)
            {
                expressions.Add(Expression.IfThen(Expression.ReferenceEqual(current, Expression.Constant(null, current.Type)),
                    Expression.Return(done, nullResult)));
            }

            var access = Expression.MakeMemberAccess(current, member);
            if (IsUnsupported(access.Type))
            {
                return null;
            }

            var value = Expression.Variable(access.Type, "member");
            variables.Add(value);
            Expression read = Expression.Assign(value, access);
            if (member is PropertyInfo)
            {
                // Reflection wraps every getter exception in TargetInvocationException. Catch
                // only the read, so comparer exceptions retain their original behavior.
                read = Expression.TryCatch(read, Expression.Catch(typeof(Exception),
                    Expression.Block(Expression.Return(done, nullResult), Expression.Default(access.Type))));
            }

            expressions.Add(read);
            current = value;
        }

        expressions.Add(Expression.Label(done, compare(current)));
        var specialized = Expression.Block(variables, expressions);
        // The fallback keeps null/incompatible-target argument errors identical to Read.
        return Expression.Condition(Expression.TypeIs(target, root), specialized, fallback);
    }

    private static bool IsUnsupported(Type type) => type.IsByRef || type.IsPointer || type.IsByRefLike || type.IsFunctionPointer;
}
