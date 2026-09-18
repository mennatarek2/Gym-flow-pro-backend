using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using FluentValidation;
using FluentValidation.AspNetCore;
using Hangfire;
using GMS.Infrastructure;
using GMS.Application;
using GMS.Platform;
using GMS.Platform.Constants;
using GMS.Platform.Persistence;
using GMS.Api.Extensions;
using GMS.Api.Middleware;
using GMS.Api.Hubs;
using GMS.Api.Filters;
using GMS.Api.Authorization;
using GMS.Infrastructure.Persistence;
using GMS.Core.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Windows Service hosting (Phase 3). Documented no-op unless the process was actually launched
// by the Service Control Manager — SaaS (IIS/MonsterASP) and plain `dotnet run` are unaffected.
builder.Host.UseWindowsService(options => options.ServiceName = "HyMotion");

builder.ConfigureProductionBasics();
var configuration = builder.Configuration;

// ── DEPLOYMENT EDITION (SaaS | Local) ─────────────────────────────────────────
// Missing/unrecognized config safely resolves to SaaS — see DeploymentOptions.ResolveEdition.
// Resolved from the base appsettings layers only (before any ProgramData override below), since
// the override file's own location depends on already knowing the edition.
builder.Services.Configure<DeploymentOptions>(configuration.GetSection(DeploymentOptions.SectionName));
var deploymentEdition = new DeploymentOptions { Edition = configuration[$"{DeploymentOptions.SectionName}:Edition"] }.ResolveEdition();
builder.Services.AddSingleton(typeof(DeploymentEdition), deploymentEdition);

// ── LOCAL RUNTIME DIRECTORIES + MACHINE-SPECIFIC CONFIG OVERRIDE ──────────────
// %ProgramData%\HyMotion\{config,uploads,secrets,logs} created upfront so every Local service
// (file storage, secrets, logging) finds them ready rather than each lazily creating its own.
// The optional config\appsettings.json there lets an installer/setup script hand this install a
// discovered SQL Server connection string (or any other machine-specific value) without ever
// committing it to source control — ConfigurationManager applies it immediately, so the
// synchronous reads below (connection string, JWT secret, ...) see it just like any other layer.
if (deploymentEdition == DeploymentEdition.Local)
{
    GMS.Core.Configuration.LocalRuntimePaths.EnsureDirectories();
    configuration.AddJsonFile(GMS.Core.Configuration.LocalRuntimePaths.ConfigOverrideFile, optional: true, reloadOnChange: false);

    // Production file logging with daily rotation + retention — a Windows Service has no
    // attached console, so this is the only place operators can see startup/runtime problems.
    // Serilog's own request-body/header enrichers are not used, so nothing here can log a
    // connection string, JWT secret, or LocalSecretProvider value — those never reach ILogger
    // as structured data in the first place (see LocalSecretProvider remarks: values are never
    // logged, only key names). Local-only: Log.Logger stays the default no-op for SaaS.
    Serilog.Log.Logger = new Serilog.LoggerConfiguration()
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
        .WriteTo.File(
            Path.Combine(GMS.Core.Configuration.LocalRuntimePaths.LogsDir, "hymotion-.log"),
            rollingInterval: Serilog.RollingInterval.Day,
            retainedFileCountLimit: 30,
            shared: true)
        .CreateLogger();
    builder.Host.UseSerilog();
}

ProductionConfigurationValidator.Validate(configuration, builder.Environment);
var connectionString = configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

// ── LOCAL PER-INSTALL SECRET BOOTSTRAP ────────────────────────────────────────
// SaaS is untouched: this only runs for Local, and only fills keys that have no value from
// appsettings/user-secrets/environment variables — an explicit operator-supplied value always
// wins. See GMS.Infrastructure/Configuration/LocalSecretProvider.cs for persistence details.
if (deploymentEdition == DeploymentEdition.Local)
{
    var requiredLocalSecretKeys = new[]
    {
        "JwtSettings:SecretKey",
        "EncryptionKey",
        "MemberAppActivation:CodePepper",
        "EmployeeAppActivation:CodePepper",
    };
    var existing = requiredLocalSecretKeys.ToDictionary(k => k, k => configuration[k]);
    using var bootstrapLoggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(b => b.AddSimpleConsole());
    var secretProvider = new GMS.Infrastructure.Configuration.LocalSecretProvider(
        bootstrapLoggerFactory.CreateLogger<GMS.Infrastructure.Configuration.LocalSecretProvider>());
    var generated = secretProvider.EnsureSecrets(requiredLocalSecretKeys, existing);
    if (generated.Count > 0)
        builder.Configuration.AddInMemoryCollection(generated!);
}

// ── CORE API ────────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHttpContextAccessor();

// Swagger with JWT
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "HyMotion API",
        Version = "v1",
        Description = "HyMotion — Gym Management System API"
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// FluentValidation
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddMemoryCache();

// ── DEBUG LOGGING (Development only — keep IIS/MonsterASP logging in Production) ──

// ── EMAIL OTP CONFIGURATION ─────────────────────────────────────────────────
// ADD THIS: Bind OtpDelivery and EmailSettings from configuration
builder.Services.Configure<GMS.Application.Options.OtpDeliveryOptions>(
    configuration.GetSection(GMS.Application.Options.OtpDeliveryOptions.SectionName));
builder.Services.Configure<GMS.Application.Options.EmailSettings>(
    configuration.GetSection(GMS.Application.Options.EmailSettings.SectionName));

// ADD THIS: Register OtpCacheService as singleton (IMemoryCache-based)
builder.Services.AddSingleton<GMS.Application.Services.OtpCacheService>();

// ADD THIS: Register IOtpDeliveryStrategy (email OTP delivery)
builder.Services.AddScoped<GMS.Application.Interfaces.IOtpDeliveryStrategy, GMS.Application.Services.EmailOtpDeliveryStrategy>();

// Layers
builder.Services.AddInfrastructure(connectionString, configuration);
builder.Services.AddApplication();
builder.Services.AddPresentation();

// ── RATE LIMITING ────────────────────────────────────────────────────────────
builder.Services.AddRateLimiter(options =>
{
    // QR Check-in: 30 requests/minute per device (by IP)
    options.AddFixedWindowLimiter("checkin-policy", limiterOptions =>
    {
        limiterOptions.PermitLimit = 30;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit = 0; // No queuing — reject immediately
    });

    // Member App activation: brute-force protection per IP
    options.AddFixedWindowLimiter("member-activate-policy", limiterOptions =>
    {
        limiterOptions.PermitLimit = 10;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit = 0;
    });

    // Employee App activation: same brute-force protection per IP
    options.AddFixedWindowLimiter("employee-activate-policy", limiterOptions =>
    {
        limiterOptions.PermitLimit = 10;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit = 0;
    });

    // HyMotion Local license activation/validation: anonymous + brute-force-guessable license
    // keys make this the highest-risk anonymous endpoint in the app - same shape as the app-
    // activation policies above, slightly tighter since a legitimate Local install only ever
    // calls this a handful of times (once at setup, then daily validation).
    options.AddFixedWindowLimiter("local-license-activate-policy", limiterOptions =>
    {
        limiterOptions.PermitLimit = 5;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit = 0;
    });

    // Desk product feedback: authenticated staff, keep spam/double-click bounded.
    options.AddFixedWindowLimiter("feedback-policy", limiterOptions =>
    {
        limiterOptions.PermitLimit = 8;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit = 0;
    });

    // Default rejection response
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = 429;
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsync(
            "{\"error\":\"لقد تجاوزت الحد المسموح به. حاول بعد دقيقة / Too many requests. Try again in a minute.\"}",
            token);
    };
});

// ── SIGNALR ──────────────────────────────────────────────────────────────────
var signalRBuilder = builder.Services.AddSignalR();
var enableRedis = configuration.GetValue<bool>("SignalR:EnableRedisBackplane");
if (enableRedis)
{
    var redisConnectionString = configuration.GetConnectionString("Redis") ?? "localhost:6379";
    signalRBuilder.AddStackExchangeRedis(redisConnectionString, options =>
    {
        options.Configuration.ChannelPrefix = new StackExchange.Redis.RedisChannel(
            configuration["SignalR:RedisChannelPrefix"] ?? "GymFlowPro",
            StackExchange.Redis.RedisChannel.PatternMode.Literal);
    });
}

// ── JWT AUTHENTICATION (tenant + platform — separate audiences) ──────────────
var jwtSecretKey = configuration["JwtSettings:SecretKey"];
if (string.IsNullOrWhiteSpace(jwtSecretKey))
    throw new InvalidOperationException("JwtSettings:SecretKey is not configured.");
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecretKey));

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = configuration["JwtSettings:Issuer"],
        ValidateAudience = true,
        ValidAudience = configuration["JwtSettings:Audience"],
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = signingKey,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };

    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            if (!string.IsNullOrEmpty(accessToken) &&
                context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        },
        OnTokenValidated = context =>
        {
            // CP6: surface impersonation claim for UI banner / audit (one-line seam on tenant JWT validation).
            var impersonatedBy = context.Principal?
                .FindFirst(GMS.Core.Constants.ImpersonationClaims.ImpersonatedByPlatformUserId)?.Value;
            if (!string.IsNullOrEmpty(impersonatedBy))
                context.HttpContext.Items[GMS.Core.Constants.ImpersonationClaims.ImpersonatedByPlatformUserId] = impersonatedBy;
            return Task.CompletedTask;
        },
        OnAuthenticationFailed = context =>
        {
            if (context.Exception.GetType() == typeof(SecurityTokenExpiredException))
                context.Response.Headers.Append("Token-Expired", "true");
            return Task.CompletedTask;
        }
    };
})
.AddJwtBearer(PlatformAuthConstants.AuthenticationScheme, options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = configuration["JwtSettings:Issuer"],
        ValidateAudience = true,
        ValidAudience = PlatformAuthConstants.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = signingKey,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero
    };
});

// Control plane (platform schema, separate migrations history, no tenant filters)
builder.Services.AddGymFlowPlatform(connectionString, configuration);

// ── LOCAL EDITION SUBSCRIPTION/FEATURE OVERRIDE ───────────────────────────────
// A Local install has no billing/tiers: swap the 3 Platform-backed services for always-unrestricted
// Local implementations. GMS.Platform itself is untouched — SaaS keeps the real implementations
// registered by AddGymFlowPlatform above. See GMS.Infrastructure/Services/Local*.cs.
if (deploymentEdition == DeploymentEdition.Local)
{
    builder.Services.Replace(ServiceDescriptor.Scoped<GMS.Core.Interfaces.IFeatureAccessService, GMS.Infrastructure.Services.LocalFeatureAccessService>());
    builder.Services.Replace(ServiceDescriptor.Scoped<GMS.Core.Interfaces.ITierEnforcementService, GMS.Infrastructure.Services.LocalTierEnforcementService>());
    builder.Services.Replace(ServiceDescriptor.Scoped<GMS.Core.Interfaces.ISubscriptionAccessService, GMS.Infrastructure.Services.LocalSubscriptionAccessService>());
    builder.Services.AddHostedService<GMS.Api.Hosting.LocalLicenseHeartbeatService>();
}

// ── AUTHORIZATION POLICIES ───────────────────────────────────────────────────
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("OwnerOnly", policy => policy.RequireRole("Owner"));
    options.AddPolicy("ManagerOrAbove", policy => policy.RequireRole("Owner", "Manager"));
    options.AddPolicy("AnyStaff", policy => policy.RequireRole("Owner", "Manager", "Trainer"));
    options.AddPolicy("AuthenticatedMember", policy => policy.RequireRole("Member"));
    options.AddPolicy("AuthenticatedEmployee", policy => policy.RequireRole("Employee"));
    options.AddPolicy("AnyAuthenticated", policy => policy.RequireAuthenticatedUser());

    // Platform-plane policies — AuthenticationSchemes locked to PlatformBearer (never tenant Bearer).
    options.AddPolicy("PlatformSupportOrAbove", policy =>
    {
        policy.AddAuthenticationSchemes(PlatformAuthConstants.AuthenticationScheme);
        policy.RequireRole(PlatformRoles.Support, PlatformRoles.Ops, PlatformRoles.Admin);
    });
    options.AddPolicy("PlatformOpsOrAbove", policy =>
    {
        policy.AddAuthenticationSchemes(PlatformAuthConstants.AuthenticationScheme);
        policy.RequireRole(PlatformRoles.Ops, PlatformRoles.Admin);
    });
    options.AddPolicy("PlatformAdminOnly", policy =>
    {
        policy.AddAuthenticationSchemes(PlatformAuthConstants.AuthenticationScheme);
        policy.RequireRole(PlatformRoles.Admin);
    });
    // Deliberately NOT part of the Support/Ops/Admin "OrAbove" chain (see PlatformRoles.Sales) -
    // Ops/Admin are added here only so they can still view sales data for oversight, not because
    // Sales sits below them in a hierarchy. License issuance/revocation/payment-verification stay
    // gated to PlatformOpsOrAbove/PlatformAdminOnly - never to this policy.
    options.AddPolicy("PlatformSalesOrAbove", policy =>
    {
        policy.AddAuthenticationSchemes(PlatformAuthConstants.AuthenticationScheme);
        policy.RequireRole(PlatformRoles.Sales, PlatformRoles.Ops, PlatformRoles.Admin);
    });
    // Sales + Support + Ops + Admin. Used for commercial customer reads/tickets.
    // Does not grant license issuance (still PlatformOpsOrAbove / PlatformAdminOnly).
    options.AddPolicy("PlatformCustomerAccess", policy =>
    {
        policy.AddAuthenticationSchemes(PlatformAuthConstants.AuthenticationScheme);
        policy.RequireRole(PlatformRoles.Sales, PlatformRoles.Support, PlatformRoles.Ops, PlatformRoles.Admin);
    });
});

// Permission-based policies (e.g. "Permission:checkin.manual") are built on demand — one
// AddPolicy per permission isn't needed. Non-"Permission:" names above still resolve normally,
// since PermissionPolicyProvider delegates to the default provider for them.
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddSingleton<IAuthorizationHandler, AnyPermissionAuthorizationHandler>();

builder.Services.AddHealthChecks();

builder.Services.AddGymFlowCors(configuration, builder.Environment);

var app = builder.Build();

// ── DATABASE SEEDING (tenant migrations first — platform.subscriptions FK → dbo.tenants) ──
try
{
    if (deploymentEdition == DeploymentEdition.Local)
        GMS.Infrastructure.Configuration.LocalDatabaseAccessRepair.TryReleaseSingleUser(connectionString);

    // 1) Tenant DB migrations first — creates dbo.tenants, AspNetRoles, etc.
    await app.ApplyProductionDatabaseMigrationsAsync();

    // 2) Platform schema migrate + seed first platform_admin
    //    (must run after tenant migrations because subscriptions FK → dbo.tenants)
    using (var scope = app.Services.CreateScope())
    {
        var platformSeeder = scope.ServiceProvider.GetRequiredService<PlatformDataSeeder>();
        platformSeeder.SeedAsync().GetAwaiter().GetResult();
    }

    // 3) Identity roles (including Member for OTP sign-in) — after tenant EF migrations.
    using (var scope = app.Services.CreateScope())
    {
        var seeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();
        seeder.EnsureIdentityRolesAsync().GetAwaiter().GetResult();
    }

    // Seed test data on startup (development only)
    if (app.Environment.IsDevelopment())
    {
        using (var scope = app.Services.CreateScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();
            var newTenantId = seeder.SeedAsync().GetAwaiter().GetResult();
            seeder.EnsureDemoOffersAsync().GetAwaiter().GetResult();

            // CP1: StartTrial after Dev seed when a new tenant is created.
            // Production gyms: POST /platform-api/tenants/provision (Platform Ops+).
            if (newTenantId.HasValue)
            {
                var subscriptions = scope.ServiceProvider.GetRequiredService<GMS.Platform.Interfaces.ISubscriptionService>();
                var trial = subscriptions.StartTrialAsync(
                    newTenantId.Value,
                    GMS.Platform.Constants.PlanTiers.Growth,
                    GMS.Platform.Constants.SubscriptionInitiators.System).GetAwaiter().GetResult();
                if (!trial.Success)
                {
                    Console.WriteLine($"⚠️ Platform trial seed skipped: {trial.ErrorCode} — {trial.ErrorMessage}");
                }
                else
                {
                    Console.WriteLine($"✅ Platform growth trial started for tenant {newTenantId}");
                }
            }
        }
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine("=== HyMotion STARTUP FAILED ===");
    Console.Error.WriteLine(ex.ToString());
    // A Windows Service has no attached console — Console.Error above is invisible when running
    // as a service. Log.Logger is only configured (non-no-op) for Local, so this is a no-op for
    // SaaS and the only place a Local operator can actually see why startup failed.
    Log.Fatal(ex, "HyMotion startup failed");
    throw;
}

app.UseGymFlowProductionPipeline();

// Rate limiting middleware
app.UseRateLimiter();

// Authentication → TenantMiddleware → Authorization (ORDER IS CRITICAL)
app.UseAuthentication();
app.UseMiddleware<TenantMiddleware>();
app.UseAuthorization();

// Hangfire dashboard
app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = new[] { new HangfireDashboardAuthFilter() },
    DashboardTitle = "HyMotion — Background Jobs"
});

app.MapHealthChecks("/health");
app.MapHub<AttendanceHub>("/hubs/attendance");
app.MapControllers();

try
{
    app.Run();
}
catch (Exception ex)
{
    // Kestrel binds its configured port lazily here (e.g. "address already in use" surfaces at
    // this point, not at builder time) — log it for Local before the process exits so a Windows
    // Service failure has a diagnosable trail in %ProgramData%\HyMotion\logs. No-op for SaaS.
    Log.Fatal(ex, "HyMotion stopped unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

// Expose for WebApplicationFactory integration tests (CP0 platform isolation).
public partial class Program { }
