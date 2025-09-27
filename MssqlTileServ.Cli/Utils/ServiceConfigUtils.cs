using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using MssqlTileServ.Cli.Models;

public static class ServiceConfigUtils
{
    public static void SetCORSOrigins(WebApplicationBuilder builder, Config config)
    {
        // Control the Cors policy based on the config
        if (config.Service.CORSOrigins.Length > 0 && config.Service.CORSOrigins[0] != "*")
        {
            builder.Services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                {
                    policy.WithOrigins(config.Service.CORSOrigins)
                          .AllowAnyHeader()
                          .AllowAnyMethod();
                });
            });
        }
        else
        {
            // Allow any origin if no specific origins are configured
            builder.Services.AddCors(options =>
            {
                options.AddDefaultPolicy(policy =>
                {
                    policy.AllowAnyOrigin()
                          .AllowAnyHeader()
                          .AllowAnyMethod();
                });
            });
        }
    }

    public static void PrintServiceConfig(Config config)
    {
        // TODO: Implement printing service config for debugging
    }
}
