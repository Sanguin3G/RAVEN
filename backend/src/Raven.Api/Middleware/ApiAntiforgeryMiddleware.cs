using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace Raven.Api.Middleware;

public static class ApiAntiforgeryMiddleware
{
    public static IApplicationBuilder UseApiAntiforgery(this IApplicationBuilder app, bool enabled)
    {
        return app.Use(async (context, next) =>
        {
            if (!enabled || !context.Request.Path.StartsWithSegments("/api") || IsSafeMethod(context.Request.Method))
            {
                await next(context);
                return;
            }

            try
            {
                await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Invalid antiforgery token",
                    Detail = "Refresh the page and try again.",
                    Instance = context.Request.Path
                }, context.RequestAborted);
                return;
            }

            await next(context);
        });
    }

    private static bool IsSafeMethod(string method) =>
        HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method)
        || HttpMethods.IsTrace(method);
}
