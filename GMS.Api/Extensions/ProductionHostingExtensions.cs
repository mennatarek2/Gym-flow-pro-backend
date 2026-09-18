using GMS.Api.Hosting;
using GMS.Core.Configuration;
using GMS.Infrastructure.Persistence;
using GMS.Platform.Persistence;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GMS.Api.Extensions;

public static class ProductionHostingExtensions
{
    public static WebApplicationBuilder ConfigureProductionBasics(this WebApplicationBuilder builder)
    {
        if (builder.Environment.IsDevelopment())
        {
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Logging.SetMinimumLevel(LogLevel.Debug);
        }

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                | ForwardedHeaders.XForwardedProto
                | ForwardedHeaders.XForwardedHost;
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();
        });

        return builder;
    }

    public static IServiceCollection AddGymFlowCors(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment env)
    {
        services.AddCors(options =>
        {
            options.AddPolicy("AllowAll", policy =>
                policy.AllowAnyOrigin()
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .WithExposedHeaders("Token-Expired"));

            var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
            options.AddPolicy("ProductionCors", policy =>
            {
                if (origins.Length > 0)
                {
                    policy.WithOrigins(origins)
                        .AllowAnyMethod()
                        .AllowAnyHeader()
                        .WithExposedHeaders("Token-Expired");
                }
                else if (env.IsProduction())
                {
                    policy.SetIsOriginAllowed(_ => true)
                        .AllowAnyMethod()
                        .AllowAnyHeader()
                        .WithExposedHeaders("Token-Expired");
                }
                else
                {
                    policy.AllowAnyOrigin()
                        .AllowAnyMethod()
                        .AllowAnyHeader()
                        .WithExposedHeaders("Token-Expired");
                }
            });
        });

        return services;
    }

    public static async Task ApplyProductionDatabaseMigrationsAsync(this WebApplication app)
    {
        if (!app.Configuration.GetValue("DatabaseConfig:ApplyMigrationsOnStartup", false))
            return;

        using var scope = app.Services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseMigration");

        var tenantDb = scope.ServiceProvider.GetRequiredService<GymFlowProDbContext>();
        logger.LogInformation("Applying EF migrations for tenant database…");
        await tenantDb.Database.MigrateWithSqlImportBaselineAsync(
            logger,
            historySchema: "dbo",
            historyTable: HistoryRepository.DefaultTableName);

        logger.LogInformation("Tenant database migrations complete.");
    }

    public static WebApplication UseGymFlowProductionPipeline(this WebApplication app)
    {
        app.UseForwardedHeaders();

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/swagger/v1/swagger.json", "HyMotion API v1");
                options.RoutePrefix = string.Empty;
            });
        }
        else if (ShouldForceHttps(app))
        {
            app.UseHsts();
        }

        if (ShouldForceHttps(app))
            app.UseHttpsRedirection();

        if (app.Configuration.GetValue("Hosting:ServeWebDashboard", true)
            && Directory.Exists(Path.Combine(app.Environment.WebRootPath ?? "", "dashboard")))
        {
            app.UseMiddleware<WebDashboardMiddleware>();
        }

        app.UseStaticFiles();

        var corsPolicy = app.Environment.IsProduction() ? "ProductionCors" : "AllowAll";
        app.UseCors(corsPolicy);

        return app;
    }

    /// <summary>
    /// Local gym desk is HTTP-only on loopback :7140. HSTS/HTTPS redirection would send
    /// /health into a port that is not listening, so the desktop icon times out.
    /// </summary>
    public static bool ShouldForceHttps(DeploymentEdition edition) =>
        edition != DeploymentEdition.Local;

    static bool ShouldForceHttps(WebApplication app)
    {
        var edition = app.Services.GetService(typeof(DeploymentEdition)) is DeploymentEdition resolved
            ? resolved
            : DeploymentEdition.SaaS;
        return ShouldForceHttps(edition);
    }
}
