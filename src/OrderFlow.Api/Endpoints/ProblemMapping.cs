using Microsoft.AspNetCore.Http.HttpResults;
using OrderFlow.Domain.Common;

namespace OrderFlow.Api.Endpoints;

internal static class ProblemMapping
{
    /// <summary>Maps an expected failure to an RFC 9457 problem response with a stable <c>code</c>.</summary>
    public static ProblemHttpResult ToProblem(this Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var statusCode = error.Kind switch
        {
            ErrorKind.Validation => StatusCodes.Status400BadRequest,
            ErrorKind.NotFound => StatusCodes.Status404NotFound,
            ErrorKind.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError,
        };
        return TypedResults.Problem(
            detail: error.Message,
            statusCode: statusCode,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }
}
