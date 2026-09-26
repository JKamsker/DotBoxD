using System.Linq.Expressions;
using System.Reflection;
using DotBoxD.Queryable.Ast;

namespace DotBoxD.Queryable.Translation;

internal static class LinqContainsCapture
{
    public static IReadOnlyList<QueryValue> Capture(object collection, MethodCallExpression call, Expression operand)
    {
        var capture = typeof(LinqContainsCapture).GetMethod(nameof(CaptureTyped), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(call.Method.GetGenericArguments()[0])
            .CreateDelegate<Func<object, MethodCallExpression, Expression, IReadOnlyList<QueryValue>>>();
        try
        {
            return capture(collection, call, operand);
        }
        catch (Exception error) when (error is not QueryTranslationException and not OperationCanceledException)
        {
            throw new QueryTranslationException(
                $"Could not capture Contains membership from the constant collection operand '{operand}'; use ToArray() when enumeration membership is intended.", error);
        }
    }

    private static IReadOnlyList<QueryValue> CaptureTyped<T>(object collection, MethodCallExpression call, Expression operand)
    {
        var values = new List<QueryValue>();
        var membership = new LinqMembershipValues<T>(call);
        foreach (var item in membership.Read((IEnumerable<T>)collection))
        {
            values.Add(QueryValueFactory.ToValue(item, operand));
        }

        return values;
    }
}
