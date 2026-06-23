using Application.Common.Models;
using Serilog;

namespace Api.Middleware;

/// <summary>Catches unhandled exceptions and returns a generic 500 — never leaks details.</summary>
public sealed class GlobalExceptionHandlerMiddleware(RequestDelegate next)
{
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<GlobalExceptionHandlerMiddleware>();

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unhandled exception processing {Method} {Path}", context.Request.Method, context.Request.Path);

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;

            var response = new ApiResponse<object> { Success = false, Error = "An unexpected error occurred" };
            await context.Response.WriteAsJsonAsync(response);
        }
    }
}
