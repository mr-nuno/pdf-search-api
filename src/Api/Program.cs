using System.Reflection;
using System.Text.Json;
using Api;
using Api.Middleware;
using Application;
using Application.Common.Models;
using FastEndpoints;
using FastEndpoints.Swagger;
using Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration));

builder.Services
    .AddApplicationServices()
    .AddInfrastructureServices(builder.Configuration)
    .AddApiServices(builder.Configuration);

var app = builder.Build();

app.UseSerilogRequestLogging(options =>
{
    options.GetLevel = (httpContext, _, _) =>
        httpContext.Request.Path.StartsWithSegments("/health")
            ? Serilog.Events.LogEventLevel.Verbose
            : Serilog.Events.LogEventLevel.Information;
});

app.UseMiddleware<GlobalExceptionHandlerMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

app.UseFastEndpoints(c =>
{
    c.Errors.ResponseBuilder = (failures, _, _) => new ApiResponse<object>
    {
        Success = false,
        ValidationErrors = failures
            .Select(f => new ValidationError(f.PropertyName, f.ErrorMessage))
            .ToList()
    };
    c.Errors.StatusCode = 400;
});

// Health checks — Kubernetes-style liveness and readiness probes.
var healthOptions = new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0";
        var response = new { status = report.Status.ToString(), version };
        await JsonSerializer.SerializeAsync(context.Response.Body, response, cancellationToken: context.RequestAborted);
    }
};
app.MapHealthChecks("/health/live", healthOptions).AllowAnonymous();
app.MapHealthChecks("/health/ready", healthOptions).AllowAnonymous();

// Development only — OpenAPI document + Scalar UI.
if (app.Environment.IsDevelopment())
{
    app.UseSwaggerGen(s => s.Path = "/openapi/{documentName}.json");
    app.MapScalarApiReference(options =>
    {
        options.OpenApiRoutePattern = "/openapi/{documentName}.json";
    });
}

app.Run();

public partial class Program;
