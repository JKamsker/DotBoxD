using System.Reflection.Emit;
using DotBoxD.Kernels.Bindings;
using DotBoxD.Kernels.Model;
using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Compiler.Emitters.Loops;

// Unboxed i32 expression plan for loop fast paths: builds a small tree from an i32 expression and emits raw IL
// (via the *I32Raw runtime helpers) with no per-node fuel metering — the loop runner charges the statically
// known FuelCost in bulk instead. Shared by I32LoopFastPathEmitter and BranchedI32LoopFastPathEmitter.
internal sealed partial class RawI32ExpressionPlan
{
    private static readonly IReadOnlyDictionary<string, SandboxFunction> NoFunctions = new Dictionary<string, SandboxFunction>(StringComparer.Ordinal);

    internal ExpressionKind Kind { get; }
    internal string? Name { get; }
    internal int Literal { get; }
    internal RawI32ExpressionPlan? Left { get; }
    internal RawI32ExpressionPlan? Right { get; }
    internal RawI32ExpressionPlan? Third { get; }

    private RawI32ExpressionPlan(
        ExpressionKind kind,
        string? name = null,
        int literal = 0,
        RawI32ExpressionPlan? left = null,
        RawI32ExpressionPlan? right = null,
        RawI32ExpressionPlan? third = null,
        int extraFuel = 0)
    {
        Kind = kind;
        Name = name;
        Literal = literal;
        Left = left;
        Right = right;
        Third = third;
        FuelCost = 1 + extraFuel + (left?.FuelCost ?? 0) + (right?.FuelCost ?? 0) + (third?.FuelCost ?? 0);
        InstructionCost =
            RawI32ExpressionPlanEmitter.BaseInstructionCost(kind) +
            (left?.InstructionCost ?? 0) +
            (right?.InstructionCost ?? 0) +
            (third?.InstructionCost ?? 0);
    }

    public int FuelCost { get; }

    public int InstructionCost { get; }

    public static bool TryCreate(Expression expression, LocalStackKindPlanner stackPlan, IReadOnlyDictionary<string, SandboxFunction> functions, out RawI32ExpressionPlan plan)
        => TryCreate(expression, stackPlan, functions, bindings: null, substitutions: null, out plan);

    public static bool TryCreate(Expression expression, LocalStackKindPlanner stackPlan, IReadOnlyDictionary<string, SandboxFunction> functions, IBindingCatalog bindings, out RawI32ExpressionPlan plan)
        => TryCreate(expression, stackPlan, functions, bindings, substitutions: null, out plan);

    private static bool TryCreate(
        Expression expression,
        LocalStackKindPlanner stackPlan,
        IReadOnlyDictionary<string, SandboxFunction> functions,
        IBindingCatalog? bindings,
        IReadOnlyDictionary<string, RawI32ExpressionPlan>? substitutions,
        out RawI32ExpressionPlan plan)
    {
        switch (expression)
        {
            case LiteralExpression literal:
                return TryCreateLiteral(literal, out plan);
            case VariableExpression variable:
                return TryCreateVariable(variable, stackPlan, substitutions, out plan);
            case UnaryExpression unary:
                return TryCreateUnary(unary, stackPlan, functions, bindings, substitutions, out plan);
            case BinaryExpression binary:
                return TryCreateBinaryExpression(binary, stackPlan, functions, bindings, substitutions, out plan);
            case CallExpression call:
                return TryCreateCallExpression(call, stackPlan, functions, bindings, substitutions, out plan);
            default:
                plan = null!;
                return false;
        }
    }

    private static bool TryCreateLiteral(LiteralExpression literal, out RawI32ExpressionPlan plan)
    {
        if (literal.Value is I32Value value)
        {
            plan = new RawI32ExpressionPlan(ExpressionKind.Literal, literal: value.Value);
            return true;
        }

        plan = null!;
        return false;
    }

    private static bool TryCreateVariable(
        VariableExpression variable,
        LocalStackKindPlanner stackPlan,
        IReadOnlyDictionary<string, RawI32ExpressionPlan>? substitutions,
        out RawI32ExpressionPlan plan)
    {
        if (substitutions is not null && substitutions.TryGetValue(variable.Name, out var substitution))
        {
            plan = substitution;
            return true;
        }

        if (stackPlan.LocalKind(variable.Name) == StackKind.I32)
        {
            plan = new RawI32ExpressionPlan(ExpressionKind.Variable, name: variable.Name);
            return true;
        }

        plan = null!;
        return false;
    }

    private static bool TryCreateUnary(
        UnaryExpression unary,
        LocalStackKindPlanner stackPlan,
        IReadOnlyDictionary<string, SandboxFunction> functions,
        IBindingCatalog? bindings,
        IReadOnlyDictionary<string, RawI32ExpressionPlan>? substitutions,
        out RawI32ExpressionPlan plan)
    {
        if (unary.Operator != "-" ||
            !TryCreate(unary.Operand, stackPlan, functions, bindings, substitutions, out var operand))
        {
            plan = null!;
            return false;
        }

        plan = new RawI32ExpressionPlan(ExpressionKind.Negate, left: operand);
        return true;
    }

    private static bool TryCreateBinaryExpression(
        BinaryExpression binary,
        LocalStackKindPlanner stackPlan,
        IReadOnlyDictionary<string, SandboxFunction> functions,
        IBindingCatalog? bindings,
        IReadOnlyDictionary<string, RawI32ExpressionPlan>? substitutions,
        out RawI32ExpressionPlan plan)
    {
        if (binary.Operator is not ("+" or "-" or "*" or "/" or "%"))
        {
            plan = null!;
            return false;
        }

        return TryCreateAddRemainder(binary, stackPlan, functions, substitutions, out plan) ||
               TryCreateBinary(binary, stackPlan, functions, bindings, substitutions, out plan);
    }

    private static bool TryCreateCallExpression(
        CallExpression call,
        LocalStackKindPlanner stackPlan,
        IReadOnlyDictionary<string, SandboxFunction> functions,
        IBindingCatalog? bindings,
        IReadOnlyDictionary<string, RawI32ExpressionPlan>? substitutions,
        out RawI32ExpressionPlan plan)
    {
        return bindings is not null &&
               TryCreateMathIntrinsic(call, stackPlan, functions, bindings, substitutions, out plan) ||
               TryCreateInlineCall(call, stackPlan, functions, bindings, out plan);
    }

    public void Emit(ILGenerator il, Func<string, (LocalBuilder Local, StackKind Kind)> declare)
        => RawI32ExpressionPlanEmitter.Emit(this, il, declare);

    private static bool TryCreateAddRemainder(BinaryExpression binary, LocalStackKindPlanner stackPlan, IReadOnlyDictionary<string, SandboxFunction> functions, IReadOnlyDictionary<string, RawI32ExpressionPlan>? substitutions, out RawI32ExpressionPlan plan)
    {
        if (substitutions is not null)
        {
            plan = null!;
            return false;
        }

        if (binary is not
            {
                Operator: "%",
                Left: BinaryExpression { Operator: "+" } add
            } ||
            !TryCreate(add.Left, stackPlan, functions, out var left) ||
            !TryCreate(add.Right, stackPlan, functions, out var right) ||
            !TryCreate(binary.Right, stackPlan, functions, out var divisor))
        {
            plan = null!;
            return false;
        }

        plan = new RawI32ExpressionPlan(
            ExpressionKind.AddRemainder,
            left: left,
            right: right,
            third: divisor,
            extraFuel: 1);
        return true;
    }

    private static bool TryCreateBinary(
        BinaryExpression binary,
        LocalStackKindPlanner stackPlan,
        IReadOnlyDictionary<string, SandboxFunction> functions,
        IBindingCatalog? bindings,
        IReadOnlyDictionary<string, RawI32ExpressionPlan>? substitutions,
        out RawI32ExpressionPlan plan)
    {
        if (!TryCreate(binary.Left, stackPlan, functions, bindings, substitutions, out var left) ||
            !TryCreate(binary.Right, stackPlan, functions, bindings, substitutions, out var right))
        {
            plan = null!;
            return false;
        }

        plan = new RawI32ExpressionPlan(
            BinaryKind(binary.Operator),
            left: left,
            right: right);
        return true;
    }

    private static ExpressionKind BinaryKind(string op)
        => op switch
        {
            "+" => ExpressionKind.Add,
            "-" => ExpressionKind.Subtract,
            "*" => ExpressionKind.Multiply,
            "/" => ExpressionKind.Divide,
            "%" => ExpressionKind.Remainder,
            _ => throw new SandboxRuntimeException(new SandboxError(SandboxErrorCode.ValidationError, "unsupported i32 expression"))
        };

    internal enum ExpressionKind
    {
        Literal,
        Variable,
        Negate,
        InlineCall,
        Add,
        Subtract,
        Multiply,
        Divide,
        Remainder,
        AddRemainder,
        Abs,
        Min,
        Max,
        Clamp
    }
}
