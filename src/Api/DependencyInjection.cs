using FastEndpoints;
using FastEndpoints.Swagger;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Api;

public static class DependencyInjection
{
    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration configuration)
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

        AddEntraIdAuthentication(services, configuration);
        services.AddAuthorization();

        return services;
    }

    // Resource-server JWT scaffolding. Functional endpoints are AllowAnonymous for now; the
    // handler only runs when an endpoint requires authorization, so empty local config is fine.
    private static void AddEntraIdAuthentication(IServiceCollection services, IConfiguration configuration)
    {
        var entra = configuration.GetSection("EntraId");
        var tenantId = entra["TenantId"];

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = entra["Authority"];
                options.Audience = entra["Audience"];
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = !string.IsNullOrWhiteSpace(tenantId),
                    // Accept both v1.0 and v2.0 issuer formats (depends on accessTokenAcceptedVersion).
                    ValidIssuers = string.IsNullOrWhiteSpace(tenantId)
                        ? null
                        :
                        [
                            $"https://sts.windows.net/{tenantId}/",
                            $"https://login.microsoftonline.com/{tenantId}/v2.0"
                        ]
                };
            });
    }
}
