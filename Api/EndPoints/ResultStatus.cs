using Microsoft.AspNetCore.Http.HttpResults;

namespace Api.EndPoints;

// Status code of an IResult, for outcome logs.
public static class ResultStatus
{
    public static string Of(IResult result) => result switch
    {
        IStatusCodeHttpResult { StatusCode: int code } => code.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ForbidHttpResult => "403",
        _ => result.GetType().Name
    };
}
