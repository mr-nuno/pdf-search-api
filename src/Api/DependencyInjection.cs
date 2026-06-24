using System.Security.Claims;
using Api.Auth;
using FastEndpoints;
using FastEndpoints.Swagger;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Api;

public static class DependencyInjection
{
    public static IServiceCollection AddApiServices(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddFastEndpoints();
        services.SwaggerDocument(o =>
        {
            o.DocumentSettings = s =>
            {
                s.Title = "PDF Content Ingestion & Full-Text Search API";
                s.Version = "v1";
            };
        });

        services.AddHealthChecks();

        if (environment.IsDevelopment())
            AddDevBypassAuthentication(services);
        else
            AddLogtoAuthentication(services, configuration);

        services.AddAuthorization(options =>
        {
            options.AddPolicy(AuthPolicy.Read, policy =>
                policy.RequireAuthenticatedUser()
                      .RequireAssertion(ctx => HasScope(ctx.User, AuthPolicy.Read)));

            options.AddPolicy(AuthPolicy.Write, policy =>
                policy.RequireAuthenticatedUser()
                      .RequireAssertion(ctx => HasScope(ctx.User, AuthPolicy.Write)));
        });

        return services;
    }

    private static void AddLogtoAuthentication(IServiceCollection services, IConfiguration configuration)
    {
        var logto = configuration.GetSection("Logto");

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = logto["Authority"];
                options.Audience = logto["Audience"];
            });
    }

    private static void AddDevBypassAuthentication(IServiceCollection services) =>
        services.AddAuthentication(DevBypassAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, DevBypassAuthHandler>(
                DevBypassAuthHandler.SchemeName, _ => { });

    // Handles both space-separated ("api:read api:write") and multi-value scope claims.
    private static bool HasScope(ClaimsPrincipal user, string scope) =>
        user.Claims
            .Where(c => c.Type == "scope")
            .Any(c => c.Value == scope || c.Value.Split(' ').Contains(scope));
}
