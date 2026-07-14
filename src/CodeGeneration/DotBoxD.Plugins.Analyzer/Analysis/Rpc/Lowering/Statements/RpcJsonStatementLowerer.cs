using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DotBoxD.Plugins.Analyzer.Analysis.Rpc;

internal static class RpcJsonStatementLowerer
{
    public static void LowerStatements(
        DotBoxDRpcJsonLowerer lowerer,
        IEnumerable<StatementSyntax> statements,
        List<string> parts)
    {
        foreach (var statement in statements)
        {
            LowerStatement(lowerer, statement, parts);
        }
    }

    public static void LowerStatement(
        DotBoxDRpcJsonLowerer lowerer,
        StatementSyntax statement,
        List<string> output)
    {
        lowerer.CancellationToken.ThrowIfCancellationRequested();
        switch (statement)
        {
            case LocalDeclarationStatementSyntax local:
                LowerLocalDeclaration(lowerer, local, output);
                break;
            case ExpressionStatementSyntax expression:
                RpcJsonExpressionStatementLowerer.LowerExpressionStatement(lowerer, expression.Expression, output);
                break;
            case ForEachStatementSyntax loop:
                LowerForEach(lowerer, loop, output);
                break;
            case WhileStatementSyntax loop:
                lowerer.LowerWhile(loop, output);
                break;
            case IfStatementSyntax branch:
                LowerIf(lowerer, branch, output);
                break;
            case ReturnStatementSyntax returned:
                LowerReturn(lowerer, returned, output);
                break;
            case ContinueStatementSyntax:
                output.Add(DotBoxDRpcJsonLowerer.Obj(("op", DotBoxDRpcJsonLowerer.Str("continue"))));
                break;
            case BreakStatementSyntax:
                output.Add(DotBoxDRpcJsonLowerer.Obj(("op", DotBoxDRpcJsonLowerer.Str("break"))));
                break;
            case BlockSyntax block:
                lowerer.LowerServiceHandleScopedBlock(block, output);
                break;
            default:
                throw new NotSupportedException($"Server extension statement '{statement.Kind()}' is not supported.");
        }
    }

    private static void LowerLocalDeclaration(
        DotBoxDRpcJsonLowerer lowerer,
        LocalDeclarationStatementSyntax local,
        List<string> output)
    {
        foreach (var declarator in local.Declaration.Variables)
        {
            if (declarator.Initializer is not { } initializer)
            {
                lowerer.ValidateUninitializedLocalDeclaration(declarator);
                continue;
            }

            var localName = declarator.Identifier.ValueText;
            if (lowerer.TryLowerServiceHandleLocal(localName, initializer.Value, output))
            {
                continue;
            }

            var localSymbol = lowerer.Model.GetDeclaredSymbol(declarator, lowerer.CancellationToken) as ILocalSymbol
                ?? throw new NotSupportedException(
                    $"Server extension local '{localName}' could not be resolved.");
            var value = lowerer.LowerExpressionWithPrelude(initializer.Value, output);
            output.Add(DotBoxDRpcJsonLowerer.SetStatement(
                localName,
                lowerer.ApplyRequiredLocalConversion(
                    initializer.Value,
                    localSymbol,
                    value,
                    local.Declaration.Type is IdentifierNameSyntax { Identifier.ValueText: "var" })));
        }
    }

    private static void LowerForEach(
        DotBoxDRpcJsonLowerer lowerer,
        ForEachStatementSyntax loop,
        List<string> output)
    {
        if (DotBoxDRpcTypeMapper.ListElementType(lowerer.TypeOf(loop.Expression)) is not { } elementType)
        {
            throw new NotSupportedException(
                $"Server extension foreach source '{loop.Expression}' must be a supported list type.");
        }

        var suffix = lowerer.NextLoopTempSuffix();
        var source = "__sir_src" + suffix;
        var index = "__sir_i" + suffix;
        output.Add(DotBoxDRpcJsonLowerer.SetStatement(
            source,
            lowerer.LowerExpressionWithPrelude(loop.Expression, output)));

        var body = new List<string>
        {
            DotBoxDRpcJsonLowerer.SetStatement(loop.Identifier.ValueText, BuildForEachItem(lowerer, loop, elementType, source, index))
        };
        LowerStatement(lowerer, loop.Statement, body);

        output.Add(DotBoxDRpcJsonLowerer.Obj(
            ("op", DotBoxDRpcJsonLowerer.Str("forRange")),
            ("local", DotBoxDRpcJsonLowerer.Str(index)),
            ("start", DotBoxDRpcJsonLowerer.I32(0)),
            ("end", DotBoxDRpcJsonLowerer.Call("list.count", null, DotBoxDRpcJsonLowerer.Var(source))),
            ("body", "[" + string.Join(",", body) + "]")));
    }

    private static string BuildForEachItem(
        DotBoxDRpcJsonLowerer lowerer,
        ForEachStatementSyntax loop,
        ITypeSymbol elementType,
        string source,
        string index)
    {
        var local = lowerer.Model.GetDeclaredSymbol(loop, lowerer.CancellationToken)
            ?? throw new NotSupportedException(
                $"Server extension foreach local '{loop.Identifier.ValueText}' could not be resolved.");
        var item = DotBoxDRpcJsonLowerer.Call(
            "list.get",
            null,
            DotBoxDRpcJsonLowerer.Var(source),
            DotBoxDRpcJsonLowerer.Var(index));
        return lowerer.ApplyNumericConversion(elementType, local.Type, item);
    }

    private static void LowerIf(
        DotBoxDRpcJsonLowerer lowerer,
        IfStatementSyntax branch,
        List<string> output)
    {
        var then = new List<string>();
        LowerStatement(lowerer, branch.Statement, then);
        var @else = new List<string>();
        if (branch.Else is { } elseClause)
        {
            LowerStatement(lowerer, elseClause.Statement, @else);
        }

        output.Add(DotBoxDRpcJsonLowerer.Obj(
            ("op", DotBoxDRpcJsonLowerer.Str("if")),
            ("condition", lowerer.LowerExpressionWithPrelude(branch.Condition, output)),
            ("then", "[" + string.Join(",", then) + "]"),
            ("else", "[" + string.Join(",", @else) + "]")));
    }

    private static void LowerReturn(
        DotBoxDRpcJsonLowerer lowerer,
        ReturnStatementSyntax returned,
        List<string> output)
    {
        if (returned.Expression is null)
        {
            output.Add(DotBoxDRpcJsonLowerer.Obj(
                ("op", DotBoxDRpcJsonLowerer.Str("return")),
                ("value", DotBoxDRpcJsonLowerer.Unit())));
            return;
        }

        var value = lowerer.LowerExpressionWithPrelude(returned.Expression, output);
        if (lowerer.ReturnValueType is not null)
        {
            value = lowerer.ApplyRequiredReturnConversion(returned.Expression, lowerer.ReturnValueType, value);
        }

        output.Add(DotBoxDRpcJsonLowerer.Obj(
            ("op", DotBoxDRpcJsonLowerer.Str("return")),
            ("value", ReturnValue(lowerer, value))));
    }

    private static string ReturnValue(DotBoxDRpcJsonLowerer lowerer, string userReturn)
    {
        if (lowerer.ReturnRecordFields.Count == 0)
        {
            return userReturn;
        }

        var fields = new string[1 + lowerer.ReturnRecordFields.Count];
        fields[0] = userReturn;
        for (var i = 0; i < lowerer.ReturnRecordFields.Count; i++)
        {
            fields[i + 1] = DotBoxDRpcJsonLowerer.Var(lowerer.ReturnRecordFields[i]);
        }

        return DotBoxDRpcJsonLowerer.Call("record.new", lowerer.ReturnRecordType, fields);
    }
}
