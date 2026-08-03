namespace DotBoxD.Plugins.Analyzer.Analysis.Registration;

internal static class RegistrationAccumulatorNameValidator
{
    private const string FlushMemberName = "FlushAsync";

    public static void ValidateAccumulatorName(string accumulatorName)
    {
        if (accumulatorName == FlushMemberName)
        {
            throw new NotSupportedException(
                $"Registration accumulator name '{accumulatorName}' collides with generated member '{FlushMemberName}'.");
        }
    }

    public static void ValidateGeneratedMemberName(
        string accumulatorName,
        string methodName,
        EquatableArray<RegistrationTypeParameterModel> typeParameters)
    {
        if (methodName == accumulatorName)
        {
            throw new NotSupportedException(
                $"Registration accumulator method '{methodName}' collides with generated type '{accumulatorName}'.");
        }

        if (methodName == FlushMemberName && typeParameters.Count == 0)
        {
            throw new NotSupportedException(
                $"Registration accumulator method '{methodName}' collides with generated member '{FlushMemberName}'.");
        }
    }
}
