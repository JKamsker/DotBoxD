using System.Collections.Generic;
using System.Threading;
using DotBoxD.Services.SourceGenerator.Infrastructure;

namespace DotBoxD.Services.SourceGenerator.Models;

internal static class FinalRejectionMethodParameters
{
    public static EquatableArray<ParameterModel> Build(MethodModel method, CancellationToken ct)
    {
        if (NamingHelpers.IsAsync(method.ReturnKind) && method.HasCancellationToken)
        {
            return method.Parameters;
        }

        var parameters = new List<ParameterModel>();
        foreach (var parameter in method.Parameters.Array)
        {
            ct.ThrowIfCancellationRequested();

            if (!parameter.IsCancellationToken)
            {
                parameters.Add(parameter);
            }
        }

        parameters.Add(new ParameterModel(
            "ct",
            ServicesGeneratorTypeNames.GlobalCancellationToken,
            ServicesGeneratorTypeNames.GlobalCancellationToken,
            IsCancellationToken: true,
            HasDefaultValue: true,
            MetadataType: ServicesGeneratorTypeNames.GlobalCancellationToken));

        return parameters.ToEquatableArray();
    }
}
