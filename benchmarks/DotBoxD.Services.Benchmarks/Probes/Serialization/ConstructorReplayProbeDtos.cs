namespace DotBoxD.Services.Benchmarks.Probes;

public sealed class ConstructorReplayStableDto
{
    public ConstructorReplayStableDto(int id) => Id = id;

    public int Id { get; }
}

public sealed class ConstructorReplayActivationDto
{
    public ConstructorReplayActivationDto(int id) => Id = id;

    public int Id { get; }
}

public sealed class ConstructorReplayActivationWarmupDto
{
    public ConstructorReplayActivationWarmupDto(int id) => Id = id;

    public int Id { get; }
}

public class ConstructorReplayBaseDto
{
    public ConstructorReplayBaseDto(int id) => Id = id;

    public int Id { get; }
}

public class ConstructorReplayActivationBaseDto
{
    public ConstructorReplayActivationBaseDto(int id) => Id = id;

    public int Id { get; }
}

public sealed class ConstructorReplayActivationDerivedDto : ConstructorReplayActivationBaseDto
{
    public ConstructorReplayActivationDerivedDto(int id)
        : base(id)
    {
    }
}

public sealed class ConstructorReplayDerivedDto : ConstructorReplayBaseDto
{
    public ConstructorReplayDerivedDto(int id)
        : base(id)
    {
    }
}

public sealed class ConstructorReplayExactDerivedControlDto : ConstructorReplayBaseDto
{
    public ConstructorReplayExactDerivedControlDto(int id)
        : base(id)
    {
    }
}

public class ConstructorReplayAlternatingBaseDto
{
    public ConstructorReplayAlternatingBaseDto(int id) => Id = id;

    public int Id { get; }
}

public sealed class ConstructorReplayAlternatingFirstDto : ConstructorReplayAlternatingBaseDto
{
    public ConstructorReplayAlternatingFirstDto(int id)
        : base(id)
    {
    }
}

public sealed class ConstructorReplayAlternatingSecondDto : ConstructorReplayAlternatingBaseDto
{
    public ConstructorReplayAlternatingSecondDto(int id)
        : base(id)
    {
    }
}

public sealed class ConstructorReplaySettableDto
{
    public int Id { get; set; }
}

public sealed class ConstructorReplayComplexDto
{
    public ConstructorReplayComplexDto(int[] values) => Values = values;

    public int[] Values { get; }
}

public class ConstructorReplayComplexBaseDto
{
    public ConstructorReplayComplexBaseDto(int[] values) => Values = values;

    public int[] Values { get; }
}

public sealed class ConstructorReplayComplexDerivedDto : ConstructorReplayComplexBaseDto
{
    public ConstructorReplayComplexDerivedDto(int[] values)
        : base(values)
    {
    }
}
