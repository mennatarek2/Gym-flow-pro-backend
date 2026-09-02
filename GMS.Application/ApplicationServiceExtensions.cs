namespace GMS.Application;

using Microsoft.Extensions.DependencyInjection;
using FluentValidation;
using GMS.Application.Interfaces;
using GMS.Application.Jobs;
using GMS.Application.Options;
using GMS.Application.Services;

/// <summary>
/// Extension methods for registering Application layer services.
/// </summary>
public static class ApplicationServiceExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddOptions<MemberAppActivationOptions>()
            .BindConfiguration(MemberAppActivationOptions.SectionName);
        services.AddOptions<EmployeeAppActivationOptions>()
            .BindConfiguration(EmployeeAppActivationOptions.SectionName);

        // Auth service
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IMemberAppActivationService, MemberAppActivationService>();
        services.AddScoped<IEmployeeAppActivationService, EmployeeAppActivationService>();

        // Domain services
        services.AddScoped<ICheckinService, CheckinService>();
        services.AddScoped<IInvitationService, InvitationService>();
        services.AddScoped<IReferralAttributionService, ReferralAttributionService>();
        services.AddScoped<IReferralRewardService, ReferralRewardService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IMemberService, MemberService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IStaffNotificationPublisher, StaffNotificationPublisher>();
        services.AddSingleton<GMS.Core.Interfaces.IStaffNotificationRealtimeNotifier, NullStaffNotificationRealtimeNotifier>();
        services.AddScoped<IMembershipPlanService, MembershipPlanService>();
        services.AddScoped<IProductCatalogService, ProductCatalogService>();
        services.AddScoped<IWarehouseService, WarehouseService>();
        services.AddScoped<IStockLedgerService, StockLedgerService>();
        services.AddScoped<IStockAdjustmentService, StockAdjustmentService>();
        services.AddScoped<ISupplierService, SupplierService>();
        services.AddScoped<IPurchaseOrderService, PurchaseOrderService>();
        services.AddScoped<IStockTransferService, StockTransferService>();
        services.AddScoped<IStockCountService, StockCountService>();
        services.AddScoped<IInventoryReportService, InventoryReportService>();
        services.AddScoped<IInventoryReorderCalculator, InventoryReorderCalculator>();
        services.AddScoped<IMemberStoreService, MemberStoreService>();
        services.AddScoped<IMembershipService, MembershipService>();
        services.AddScoped<IAdminService, AdminService>();
        services.AddScoped<IRolePermissionService, RolePermissionService>();
        services.AddScoped<ITenantProvisioningService, TenantProvisioningService>();
        services.AddScoped<ITenantSettingsService, TenantSettingsService>();
        services.AddScoped<IGymOccupancyService, GymOccupancyService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IPlatformImpersonationService, PlatformImpersonationService>();
        services.AddScoped<IPlatformTenantStaffService, PlatformTenantStaffService>();
        services.AddScoped<IPromoService, PromoService>();
        services.AddScoped<IOfferService, OfferService>();
        services.AddScoped<IShiftService, ShiftService>();
        services.AddScoped<IInvoiceService, InvoiceService>();
        services.AddScoped<IInvoiceDeliveryJob, RenderAndDeliverInvoiceJob>();
        services.AddScoped<ISaleService, SaleService>();
        services.AddScoped<IZReportService, ZReportService>();
        services.AddScoped<ITrialService, TrialService>();
        services.AddScoped<IRefundService, RefundService>();
        services.AddScoped<ISaleAdjustmentService, SaleAdjustmentService>();
        services.AddScoped<IDebtorsService, DebtorsService>();
        services.AddScoped<ICallSheetService, CallSheetService>();
        services.AddScoped<IActivityService, ActivityService>();
        services.AddScoped<ISessionBookingService, SessionBookingService>();
        services.AddScoped<IActivityEntitlementService, ActivityEntitlementService>();
        services.AddScoped<IMemberBookingService, MemberBookingService>();
        services.AddScoped<IMemberClassService, MemberClassService>();
        services.AddScoped<IDropInService, DropInService>();
        services.AddScoped<ISessionGenerationService, SessionGenerationService>();
        services.AddScoped<IImportService, ImportService>();

        // HR / Staff Workforce (Phase 2: Foundation)
        services.AddScoped<IDepartmentService, DepartmentService>();
        services.AddScoped<IPositionService, PositionService>();
        services.AddScoped<IEmployeeService, EmployeeService>();
        services.AddScoped<IEmployeeShiftService, EmployeeShiftService>();
        services.AddScoped<IEmployeeScheduleService, EmployeeScheduleService>();
        services.AddScoped<IEmployeeAttendanceService, EmployeeAttendanceService>();
        services.AddScoped<ILeaveBalanceService, LeaveBalanceService>();
        services.AddScoped<ILeaveRequestService, LeaveRequestService>();
        services.AddScoped<IPayrollPeriodService, PayrollPeriodService>();
        services.AddScoped<IPayrollAdjustmentService, PayrollAdjustmentService>();
        services.AddScoped<IPayrollPaymentService, PayrollPaymentService>();
        services.AddScoped<IEmployeeDocumentService, EmployeeDocumentService>();
        services.AddScoped<IHrDashboardService, HrDashboardService>();

        // Analytics & Reports services
        services.AddScoped<IAnalyticsService, AnalyticsService>();
        services.AddScoped<IReportsService, ReportsService>();
        services.AddScoped<IProfitabilityService, ProfitabilityService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<ICashExpenseService, CashExpenseService>();

        // Z-Report / guest-pass expiry schedulers live here (not Infrastructure JobScheduler)
        // because they depend on Application-layer services.
        services.AddHostedService<ZReportJobScheduler>();
        services.AddHostedService<GuestPassExpiryJobScheduler>();
        services.AddHostedService<SessionGenerationJobScheduler>();
        services.AddHostedService<ProcessReferralRewardHoldsJobScheduler>();
        services.AddHostedService<InventoryLowStockJobScheduler>();
        services.AddHostedService<StaffNotificationReminderJobScheduler>();

        // FluentValidation — auto-register all validators from this assembly
        services.AddValidatorsFromAssembly(typeof(ApplicationServiceExtensions).Assembly);

        return services;
    }
}
