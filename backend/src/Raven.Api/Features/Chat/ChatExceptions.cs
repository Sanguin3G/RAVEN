using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Raven.Api.Features.Chat;

public sealed class ChatProblemException(int statusCode, string code, string title, string detail) : Exception(detail)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
    public string ProblemTitle { get; } = title;
    public string ProblemDetail { get; } = detail;
}

public sealed class ChatExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not ChatProblemException failure) return false;
        var problem = new ProblemDetails
        {
            Status = failure.StatusCode,
            Title = failure.ProblemTitle,
            Detail = failure.ProblemDetail,
            Instance = httpContext.Request.Path
        };
        problem.Extensions["code"] = failure.Code;
        httpContext.Response.StatusCode = failure.StatusCode;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }
}
