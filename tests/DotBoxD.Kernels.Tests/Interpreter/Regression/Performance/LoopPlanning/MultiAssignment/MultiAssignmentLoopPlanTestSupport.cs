using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Interpreter.Frame;
using DotBoxD.Kernels.Interpreter.Internal;
using DotBoxD.Kernels.Interpreter.Internal.Expressions;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Tests.Interpreter.Regression.Performance.MultiAssignment;

internal static class MultiAssignmentLoopPlanTestSupport
{
    private const long LoopFuel = 5;

    public static SourceSpan Span { get; } = new(1, 1);

    public static AssignmentStatement Assign(string name, Expression expression)
        => new(name, expression, Span);

    public static BinaryExpression Add(Expression left, Expression right)
        => new(left, "+", right, Span);

    public static BinaryExpression LessThan(Expression left, Expression right)
        => new(left, "<", right, Span);

    public static LiteralExpression Literal(int value)
        => new(SandboxValue.FromInt32(value), Span);

    public static VariableExpression Variable(string name)
        => new(name, Span);

    public static ForRangeStatement ForRange(params Statement[] body)
        => new("i", Literal(0), Literal(1), body, Span);

    public static WhileStatement While(Expression condition, params Statement[] body)
        => new(condition, body, Span);

    public static FunctionSetup CreateFunction(
        IReadOnlyList<Statement> statements,
        params string[] locals)
    {
        var body = new Statement[locals.Length + statements.Count];
        for (var i = 0; i < locals.Length; i++)
        {
            body[i] = Assign(locals[i], Literal(0));
        }

        for (var i = 0; i < statements.Count; i++)
        {
            body[locals.Length + i] = statements[i];
        }

        var function = new SandboxFunction(
            "main",
            true,
            [],
            SandboxType.I32,
            body);
        var layout = FunctionFrameLayout.Build(
            function,
            new Dictionary<string, FunctionAnalysis>(StringComparer.Ordinal),
            new BindingRegistryBuilder().Build());
        return new FunctionSetup(layout, function, locals);
    }

    public static InterpreterFrame CreateFrame(
        FunctionSetup setup,
        params string[] assignedLocals)
    {
        var frame = InterpreterFrame.Create(
            setup.Layout,
            setup.Function,
            LocalFunctionArguments.FromArray([]));
        foreach (var local in assignedLocals)
        {
            frame.WriteInt32(local, 1);
        }

        return frame;
    }

    public static InterpreterFrame CreateFullyAssignedFrame(FunctionSetup setup)
        => CreateFrame(setup, setup.Locals);

    public static I32ForLoopPlan CreateForPlan(
        ForRangeStatement statement,
        InterpreterFrame frame)
    {
        var body = CreateBodyPlan(statement.Body, frame, statement.LocalName, out var bodyFuel);
        return new I32ForLoopPlan(statement, body, LoopFuel + bodyFuel);
    }

    public static I32WhileLoopPlan CreateWhilePlan(
        WhileStatement statement,
        InterpreterFrame frame)
    {
        if (!I32ComparisonPlan.TryCreate(statement.Condition, frame, "", out var condition))
        {
            throw new InvalidOperationException("test while condition was not plannable as raw I32");
        }

        var body = CreateBodyPlan(statement.Body, frame, "", out var bodyFuel);
        return new I32WhileLoopPlan(statement, condition, body, bodyFuel);
    }

    private static I32LoopAssignmentPlan[] CreateBodyPlan(
        IReadOnlyList<Statement> statements,
        InterpreterFrame frame,
        string assumedInt32Local,
        out long bodyFuel)
    {
        var assignments = new I32LoopAssignmentPlan[statements.Count];
        bodyFuel = 0;
        for (var i = 0; i < statements.Count; i++)
        {
            if (statements[i] is not AssignmentStatement assignment ||
                !I32ExpressionPlan.TryCreate(
                    assignment.Value,
                    frame,
                    assumedInt32Local,
                    out var expression))
            {
                throw new InvalidOperationException("test loop body was not plannable as raw I32");
            }

            var targetSlot = frame.GetSlot(assignment.Name);
            if (!frame.IsInt32Slot(targetSlot))
            {
                throw new InvalidOperationException("test loop target did not have a raw I32 slot");
            }

            assignments[i] = new I32LoopAssignmentPlan(targetSlot, expression);
            bodyFuel += 1 + expression.FuelCost;
        }

        return assignments;
    }

    internal sealed record FunctionSetup(
        FunctionFrameLayout Layout,
        SandboxFunction Function,
        string[] Locals);
}
