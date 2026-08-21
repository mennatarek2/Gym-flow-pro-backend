namespace GMS.Platform;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using GMS.Core.Interfaces;
using GMS.Platform.Entities;
using GMS.Platform.Interfaces;
using GMS.Platform.Persistence;
using GMS.Platform.Services;

public static class PlatformServiceExtensions
{
    public const string MigrationsHistoryTable = "__PlatformMigrationsHistory";
    public const string Schema = "platform";

    public static IServiceCollection AddGymFlowPlatform(
        this IServiceCollection services,
        string connectionString,
        IConfiguration configuration)
    {
        services.AddDataProtection();

        services.AddDbContext<PlatformDbContext>(options =>
        {
            options.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(PlatformDbContext).Assembly.FullName);
                sql.MigrationsHistoryTable(MigrationsHistoryTable, Schema);
            });
        });

        services.AddScoped<IPasswordHasher<PlatformAdminUser>, PasswordHasher<PlatformAdminUser>>();
        services.AddScoped<IPlatformTokenService, PlatformTokenService>();
        services.AddScoped<IPlatformMfaSecretStore, LocalEncryptedPlatformMfaSecretStore>();
        services.AddScoped<IPlatformAuditService, PlatformAuditService>();
        services.AddScoped<IPlatformAuthService, PlatformAuthService>();
        services.AddScoped<ISubscriptionWriteRepository, SubscriptionWriteRepository>();
        services.AddScoped<ISubscriptionStatusCache, SubscriptionStatusCache>();
        services.AddHttpClient<PlatformMerchantPaymobService>(client =>
        {
            client.BaseAddress = new Uri("https://accept.paymob.com/");
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });
        services.AddHttpClient<PlatformMerchantFawryService>(client =>
        {
            client.BaseAddress = new Uri("https://www.atfawry.com/");
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });
        services.AddScoped<IPlatformBillingPaymentService, PlatformBillingPaymentService>();
        services.AddScoped<IPlatformInvoiceService, PlatformInvoiceService>();
        services.AddScoped<IPlatformProrationInvoiceService, PlatformInvoiceService>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();
        services.AddScoped<IPlatformTenantReadService, PlatformTenantReadService>();
        services.AddScoped<IPlatformTenantAdminService, PlatformTenantAdminService>();
        services.AddScoped<ITenantHealthScoreService, TenantHealthScoreService>();
        services.AddScoped<IComputeTenantHealthScoresJob, TenantHealthScoreService>();
        services.AddScoped<IPlatformRiskQueueService, PlatformRiskQueueService>();
        services.AddScoped<IPlatformMetricsService, PlatformMetricsService>();
        services.AddScoped<IProcessSubscriptionRenewalsJob, ProcessSubscriptionRenewalsJob>();
        services.AddScoped<IFeatureAccessService, FeatureAccessService>();
        services.AddScoped<ITierEnforcementService, TierEnforcementService>();
        services.AddScoped<IRollUpTenantUsageJob, RollUpTenantUsageJob>();
        services.AddScoped<IAutomationEnrollmentService, AutomationEnrollmentService>();
        services.AddScoped<IAutomationSequenceHandler, PlatformInvoiceDunningHandler>();
        services.AddScoped<IProcessAutomationEnrollmentsJob, ProcessAutomationEnrollmentsJob>();
        services.AddScoped<ISubscriptionAccessService, SubscriptionAccessService>();
        services.AddScoped<PlatformDataSeeder>();
        services.AddHostedService<PlatformRenewalJobScheduler>();
        services.AddHostedService<PlatformUsageJobScheduler>();
        services.AddHostedService<PlatformAutomationJobScheduler>();
        services.AddHostedService<PlatformHealthJobScheduler>();

        return services;
    }
}
