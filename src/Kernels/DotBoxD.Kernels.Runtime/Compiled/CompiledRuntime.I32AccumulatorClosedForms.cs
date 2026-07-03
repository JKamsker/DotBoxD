using DotBoxD.Kernels.Sandbox;

namespace DotBoxD.Kernels.Runtime;

public static partial class CompiledRuntime
{
    public static bool CanUseModuloBranchAccumulatorRaw(
        SandboxContext context,
        int current,
        int iterations,
        int divisor,
        int match,
        int thenDelta,
        int elseDelta,
        int loopFuel,
        int thenFuel,
        int elseFuel)
    {
        if (iterations <= 0 ||
            divisor <= 0 ||
            !SameDirection(thenDelta, elseDelta))
        {
            return false;
        }

        var thenCount = ModuloMatchesFromZero(iterations, divisor, match);
        var elseCount = iterations - thenCount;
        return TryAddScaled(current, thenDelta, thenCount, elseDelta, elseCount, out _) &&
               TryGetBranchFuel(thenCount, thenFuel, elseCount, elseFuel, out var branchFuel) &&
               CanUseCombinedLoopAndFuelBudget(context, iterations, loopFuel, branchFuel);
    }

    public static int AddModuloBranchDeltasI32LoopRaw(
        SandboxContext context,
        int current,
        int iterations,
        int divisor,
        int match,
        int thenDelta,
        int elseDelta,
        int loopFuel,
        int thenFuel,
        int elseFuel)
    {
        if (iterations <= 0)
        {
            return current;
        }

        if (divisor <= 0)
        {
            throw InvalidInput("integer division by zero");
        }

        var thenCount = ModuloMatchesFromZero(iterations, divisor, match);
        var elseCount = iterations - thenCount;
        if (!SameDirection(thenDelta, elseDelta) ||
            !TryAddScaled(current, thenDelta, thenCount, elseDelta, elseCount, out var result) ||
            !TryGetBranchFuel(thenCount, thenFuel, elseCount, elseFuel, out var branchFuel))
        {
            throw InvalidInput("integer overflow");
        }

        context.ChargeLoopIterations(iterations, loopFuel);
        context.ChargeBulkFuel(branchFuel, 1);
        return result;
    }

    public static bool CanUseModuloIndexAccumulatorRaw(
        SandboxContext context,
        int current,
        int index,
        int end,
        int divisor,
        int conditionFuel,
        int loopFuel)
    {
        var iterationCount = (long)end - index;
        return iterationCount is > 0 and <= int.MaxValue &&
               divisor > 0 &&
               index >= 0 &&
               current >= 0 &&
               current < divisor &&
               (long)divisor - 1 + end - 1 <= int.MaxValue &&
               TryGetScaledFuel(conditionFuel, iterationCount + 1, out var conditionFuelTotal) &&
               CanUseCombinedLoopAndFuelBudget(context, iterationCount, loopFuel, conditionFuelTotal);
    }

    public static int AddModuloIndexAccumulatorI32LoopRaw(
        SandboxContext context,
        int current,
        int index,
        int end,
        int divisor,
        int conditionFuel,
        int loopFuel)
    {
        var iterationCount = (long)end - index;
        if (iterationCount <= 0)
        {
            return current;
        }

        if (divisor <= 0)
        {
            throw InvalidInput("integer division by zero");
        }

        if (iterationCount > int.MaxValue)
        {
            throw InvalidInput("integer overflow");
        }

        var iterations = (int)iterationCount;
        if (index < 0 ||
            current < 0 ||
            current >= divisor ||
            (long)divisor - 1 + end - 1 > int.MaxValue)
        {
            throw InvalidInput("integer overflow");
        }

        context.ChargeBulkFuel(conditionFuel, iterationCount + 1);
        context.ChargeLoopIterations(iterationCount, loopFuel);
        return AddArithmeticSeriesModulo(current, index, iterations, divisor);
    }

    private static int AddArithmeticSeriesModulo(int current, int start, int count, int divisor)
    {
        var firstPlusLast = (long)start + start + count - 1;
        var terms = (long)count;
        if ((terms & 1) == 0)
        {
            terms /= 2;
        }
        else
        {
            firstPlusLast /= 2;
        }

        var series = ((terms % divisor) * (firstPlusLast % divisor)) % divisor;
        return (int)((current + series) % divisor);
    }

    private static int ModuloMatchesFromZero(int iterations, int divisor, int match)
    {
        if (match < 0 || match >= divisor)
        {
            return 0;
        }

        return iterations / divisor + (match < iterations % divisor ? 1 : 0);
    }

    private static bool SameDirection(int left, int right)
        => left == 0 || right == 0 || left > 0 == right > 0;

    private static bool CanUseCombinedLoopAndFuelBudget(
        SandboxContext context,
        long iterations,
        int loopFuel,
        long extraFuel)
        => TryGetCombinedFuel(iterations, loopFuel, extraFuel, out var totalFuel) &&
           context.CanBulkChargeLoopIterations(iterations, loopFuel) &&
           context.CanBulkChargeFuel(totalFuel, 1);

    private static bool TryGetCombinedFuel(long iterations, int loopFuel, long extraFuel, out long fuel)
    {
        if (iterations < 0 || loopFuel <= 0 || extraFuel < 0)
        {
            fuel = 0;
            return false;
        }

        try
        {
            fuel = checked(iterations * loopFuel + extraFuel);
            return true;
        }
        catch (OverflowException)
        {
            fuel = 0;
            return false;
        }
    }

    private static bool TryGetScaledFuel(long fuelPerUnit, long units, out long fuel)
    {
        if (fuelPerUnit < 0 || units < 0)
        {
            fuel = 0;
            return false;
        }

        try
        {
            fuel = checked(fuelPerUnit * units);
            return true;
        }
        catch (OverflowException)
        {
            fuel = 0;
            return false;
        }
    }

    private static bool TryAddScaled(
        int current,
        int thenDelta,
        int thenCount,
        int elseDelta,
        int elseCount,
        out int result)
    {
        long total;
        try
        {
            var delta = checked((long)thenDelta * thenCount + (long)elseDelta * elseCount);
            total = current + delta;
        }
        catch (OverflowException)
        {
            result = 0;
            return false;
        }

        if (total < int.MinValue || total > int.MaxValue)
        {
            result = 0;
            return false;
        }

        result = (int)total;
        return true;
    }

    private static bool TryGetBranchFuel(
        int thenCount,
        int thenFuel,
        int elseCount,
        int elseFuel,
        out long fuel)
    {
        try
        {
            fuel = checked((long)thenCount * thenFuel + (long)elseCount * elseFuel);
            return fuel >= 0;
        }
        catch (OverflowException)
        {
            fuel = 0;
            return false;
        }
    }
}
