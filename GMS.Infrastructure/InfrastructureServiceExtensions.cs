namespace GMS.Infrastructure;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Hangfire;
using Hangfire.SqlServer;
using Polly;
using Polly.Extensions.Http;
using GMS.Core.Entities.Identity;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Repositories;
using GMS.Infrastructure.Services;
using GMS.Infrastructure.Jobs;

/// <summary>
/// Extension methods for registering Infrastructure layer services.
/// </summary>
public static class InfrastructureServiceExtensions
{
    // Shared Polly retry policy: 3 retries on 5xx and transient errors, exponential backoff
    private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy() =>
        HttpPolicyExtensions
            .HandleTransientHttpError() // 5xx + network errors + timeouts
            .WaitAndRetryAsync(3, retryAttempt =>
                TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), // 2s, 4s, 8s
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    // Logged per-service, no logger access here
                });

    /// <summary>
    /// Local kits sometimes point LicenseServer at localhost while SaaS Debug uses the ASP.NET
    /// HTTPS development certificate. Accept that cert for loopback only — public BaseUrls
    /// (ngrok/cloud) still require a normal trusted chain.
    /// </summary>
    private static HttpClientHandler CreateLicenseServerHandler()
    {
        var handler = new HttpClientHandler();
        handler.ServerCertificateCustomValidationCallback = static (message, _, _, errors) =>
        {
            if (errors == System.Net.Security.SslPolicyErrors.None) return true;
            var host = message.RequestUri?.Host;
            return host is "localhost" or "127.0.0.1";
        };
        return handler;
    }

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString,
        IConfiguration configuration)
    {
        // Register DbContext
        services.AddDbContext<GymFlowProDbContext>((serviceProvider, options) =>
        {
            options.UseSqlServer(connectionString,
                sqlOptions => sqlOptions.MigrationsAssembly(typeof(InfrastructureServiceExtensions).Assembly.FullName));
        });

        // Redis-backed distributed cache — optional (MonsterASP: use memory cache unless Redis add-on).
        var useRedis = configuration.GetValue("Caching:UseRedis", false);
        var redisConnectionString = configuration.GetConnectionString("Redis");
        if (useRedis && !string.IsNullOrWhiteSpace(redisConnectionString))
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConnectionString;
                options.InstanceName = "GymFlowPro:";
            });
        }
        else
        {
            services.AddDistributedMemoryCache();
        }

        // ASP.NET Core Identity
        services.AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
        {
            options.Password.RequireDigit = true;
            options.Password.RequireLowercase = true;
            options.Password.RequireUppercase = true;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequiredLength = 6;

            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.AllowedForNewUsers = true;

            options.User.RequireUniqueEmail = true;
        })
        .AddEntityFrameworkStores<GymFlowProDbContext>()
        .AddDefaultTokenProviders();

        // ── TYPED HTTP CLIENTS ──────────────────────────────────────────────

        // 4jawaly WhatsApp (typed client)
        services.AddHttpClient<IWhatsAppService, FourJawalyWhatsAppService>(client =>
        {
            client.BaseAddress = new Uri("https://api.4jawaly.com/");
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        })
        .AddPolicyHandler(GetRetryPolicy());

        // Paymob payment gateway
        services.AddHttpClient<IPaymobService, PaymobService>(client =>
        {
            client.BaseAddress = new Uri("https://accept.paymob.com/");
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        })
        .AddPolicyHandler(GetRetryPolicy());

        // Fawry payment gateway
        services.AddHttpClient<IFawryService, FawryService>(client =>
        {
            client.BaseAddress = new Uri("https://www.atfawry.com/");
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        })
        .AddPolicyHandler(GetRetryPolicy());

        // ── HANGFIRE ────────────────────────────────────────────────────────
        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseSqlServerStorage(connectionString, new SqlServerStorageOptions
            {
                CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
                SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
                QueuePollInterval = TimeSpan.FromSeconds(15),
                UseRecommendedIsolationLevel = true,
                DisableGlobalLocks = true,
                SchemaName = "HangFire"
            }));

        services.AddHangfireServer(options =>
        {
            options.WorkerCount = configuration.GetValue("Hangfire:WorkerCount", 2);
        });
        services.AddHostedService<JobScheduler>();

        // ── REPOSITORIES ────────────────────────────────────────────────────
        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<IMemberRepository, MemberRepository>();
        services.AddScoped<IAttendanceRepository, AttendanceRepository>();
        services.AddScoped<IInvitationRepository, InvitationRepository>();

        // ── SERVICES ────────────────────────────────────────────────────────
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IOtpService, OtpService>();
        services.AddScoped<IOtpSender, MockOtpSender>();

        // Permission-based authorization
        services.AddScoped<IPermissionProvider, DefaultPermissionProvider>();
        services.AddScoped<IPermissionCacheService, RedisPermissionCacheService>();

        // File storage (local for dev)
        services.AddScoped<IFileStorageService, LocalFileStorageService>();

        // Invoice PDF rendering (QuestPDF + QRCoder)
        services.AddScoped<IInvoicePdfRenderer, InvoicePdfRenderer>();

        // Z-Report PDF rendering (QuestPDF)
        services.AddScoped<IZReportPdfRenderer, ZReportPdfRenderer>();

        // Push notifications (FCM mock)
        services.AddScoped<IPushNotificationService, FirebasePushService>();

        // AES-256 encryption for sensitive fields (NationalId)
        services.AddSingleton<IEncryptionService, AesEncryptionService>();

        // HyMotion Local licensing - client-side signature verification (public key only, see
        // LicenseVerificationService's class remarks; the matching private-key signer lives only
        // in GMS.Platform, never referenced from here).
        services.AddSingleton<ILicenseVerificationService, LicenseVerificationService>();
        services.AddSingleton<GMS.Infrastructure.Configuration.LocalLicenseStore>();
        services.AddSingleton<GMS.Infrastructure.Configuration.LocalDeviceSessionStore>();
        services.AddSingleton<GMS.Infrastructure.Configuration.LocalOwnerRecoveryStore>();
        services.AddHttpClient("license-server", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.TryAddWithoutValidation("ngrok-skip-browser-warning", "true");
        }).ConfigurePrimaryHttpMessageHandler(CreateLicenseServerHandler);
        services.AddHttpClient<ILocalLicenseClientService, LocalLicenseClientService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.TryAddWithoutValidation("ngrok-skip-browser-warning", "true");
        }).ConfigurePrimaryHttpMessageHandler(CreateLicenseServerHandler);

        // In dev: also register mock WhatsApp so Hangfire jobs that resolve
        // IWhatsAppService via DI (not typed client) still work
        // NOTE: FourJawalyWhatsAppService is registered as typed client above,
        // which creates a named registration. For Scoped DI injection we add:
        services.AddScoped<MockWhatsAppService>();

        // ── DATA SEEDING ────────────────────────────────────────────────────
        services.AddScoped<DataSeeder>();

        return services;
    }
}
