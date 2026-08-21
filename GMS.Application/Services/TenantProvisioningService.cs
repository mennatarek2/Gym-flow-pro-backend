namespace GMS.Application.Services;

using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Application.Common;
using GMS.Application.DTOs.Provisioning;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Entities.Identity;
using GMS.Infrastructure.Persistence;
using GMS.Platform.Constants;
using GMS.Platform.Interfaces;

/// <summary>
/// Production tenant provisioning for Platform Ops+.
/// Creates Tenant, Owner Identity user + domain AppUser, default settings/plans, starts Growth trial.
/// </summary>
public class TenantProvisioningService : ITenantProvisioningService
{
    private readonly GymFlowProDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;
    private readonly ISubscriptionService _subscriptions;
    private readonly IPlatformAuditService _platformAudit;
    private readonly ILogger<TenantProvisioningService> _logger;

    public TenantProvisioningService(
        GymFlowProDbContext db,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        ISubscriptionService subscriptions,
        IPlatformAuditService platformAudit,
        ILogger<TenantProvisioningService> logger)
    {
        _db = db;
        _userManager = userManager;
        _roleManager = roleManager;
        _subscriptions = subscriptions;
        _platformAudit = platformAudit;
        _logger = logger;
    }

    public async Task<Result<ProvisionTenantResponse>> ProvisionAsync(
        ProvisionTenantRequest request,
        Guid actorPlatformUserId,
        string? ipAddress = null,
        CancellationToken cancellationToken = default)
    {
        var ownerEmail = request.OwnerEmail.Trim().ToLowerInvariant();
        var gymEmail = request.Email.Trim().ToLowerInvariant();
        var tier = string.IsNullOrWhiteSpace(request.Tier)
            ? PlanTiers.Growth
            : request.Tier.Trim().ToLowerInvariant();

        if (!PlanTiers.All.Contains(tier))
            return Result<ProvisionTenantResponse>.Failure($"Invalid plan tier '{request.Tier}'.");

        await EnsureIdentityRolesAsync(cancellationToken);

        var existingOwner = await _userManager.FindByEmailAsync(ownerEmail);
        if (existingOwner != null)
        {
            return Result<ProvisionTenantResponse>.Failure(
                "Owner email is already registered / البريد الإلكتروني للمالك مسجل بالفعل");
        }

        var gymCode = await ResolveUniqueGymCodeAsync(request.GymCode, request.City, cancellationToken);
        if (gymCode == null)
        {
            return Result<ProvisionTenantResponse>.Failure(
                "Gym code is already in use / كود الصالة مستخدم بالفعل");
        }

        var tenantId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var tenant = new Tenant
            {
                Id = tenantId,
                Name = request.Name.Trim(),
                NameAr = string.IsNullOrWhiteSpace(request.NameAr) ? request.Name.Trim() : request.NameAr.Trim(),
                GymCode = gymCode,
                City = request.City.Trim(),
                Address = request.Address?.Trim() ?? string.Empty,
                PhoneNumber = request.PhoneNumber.Trim(),
                Email = gymEmail,
                TimeZone = string.IsNullOrWhiteSpace(request.TimeZone) ? "Africa/Cairo" : request.TimeZone.Trim(),
                Currency = "EGP",
                IsActive = true,
                SubscriptionStartDate = DateTime.UtcNow,
                Settings = BuildDefaultSettingsJson(),
                CreatedAtUtc = DateTime.UtcNow
            };
            _db.Tenants.Add(tenant);

            SeedDefaultPlans(tenantId);

            var nameParts = request.OwnerFullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var owner = new ApplicationUser
            {
                Id = ownerId,
                UserName = ownerEmail,
                Email = ownerEmail,
                EmailConfirmed = true,
                FirstName = nameParts.FirstOrDefault() ?? request.OwnerFullName.Trim(),
                LastName = nameParts.Length > 1 ? string.Join(" ", nameParts.Skip(1)) : string.Empty,
                TenantId = tenantId,
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            };

            var createResult = await _userManager.CreateAsync(owner, request.OwnerPassword);
            if (!createResult.Succeeded)
            {
                var errors = string.Join(", ", createResult.Errors.Select(e => e.Description));
                await tx.RollbackAsync(cancellationToken);
                return Result<ProvisionTenantResponse>.Failure(
                    "Failed to create owner account / فشل إنشاء حساب المالك", errors);
            }

            var roleResult = await _userManager.AddToRoleAsync(owner, "Owner");
            if (!roleResult.Succeeded)
            {
                await tx.RollbackAsync(cancellationToken);
                return Result<ProvisionTenantResponse>.Failure("Failed to assign Owner role.");
            }

            _db.AppUsers.Add(new AppUser
            {
                TenantId = tenantId,
                UserId = owner.Id.ToString(),
                FirstName = owner.FirstName,
                LastName = owner.LastName,
                Email = owner.Email!,
                Role = "Owner",
                IsActive = true,
                CreatedAtUtc = DateTime.UtcNow
            });

            _db.AuditEvents.Add(new AuditEvent
            {
                TenantId = tenantId,
                Action = "tenant.provisioned",
                ActorUserId = null, // Platform-initiated; AspNet owner exists but AppUser PK differs
                EntityType = nameof(Tenant),
                EntityId = tenantId,
                AfterJson = JsonSerializer.Serialize(new
                {
                    tenant.GymCode,
                    tenant.Name,
                    OwnerEmail = ownerEmail,
                    tier,
                    ActorPlatformUserId = actorPlatformUserId
                }),
                IpAddress = ipAddress,
                CreatedAtUtc = DateTime.UtcNow
            });

            await _db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Tenant provisioning failed for {OwnerEmail}", ownerEmail);
            return Result<ProvisionTenantResponse>.Failure(
                "Provisioning failed / فشل إنشاء الصالة", ex.Message);
        }

        var trial = await _subscriptions.StartTrialAsync(
            tenantId,
            tier,
            SubscriptionInitiators.PlatformAdmin,
            actorPlatformUserId,
            cancellationToken);

        if (!trial.Success)
        {
            _logger.LogWarning(
                "Tenant {TenantId} provisioned but StartTrial failed: {Code} {Message}",
                tenantId, trial.ErrorCode, trial.ErrorMessage);
        }

        await _platformAudit.LogAsync(
            actorPlatformUserId,
            "tenant.provision",
            tenantId,
            before: null,
            after: new
            {
                tenantId,
                gymCode,
                ownerEmail,
                tier,
                trialStarted = trial.Success,
                trialError = trial.Success ? null : $"{trial.ErrorCode}:{trial.ErrorMessage}"
            },
            ipAddress: ipAddress);

        return Result<ProvisionTenantResponse>.Success(new ProvisionTenantResponse
        {
            TenantId = tenantId,
            GymCode = gymCode!,
            OwnerUserId = ownerId,
            OwnerEmail = ownerEmail,
            TrialStarted = trial.Success,
            TrialError = trial.Success ? null : $"{trial.ErrorCode}: {trial.ErrorMessage}"
        });
    }

    private async Task EnsureIdentityRolesAsync(CancellationToken ct)
    {
        foreach (var roleName in new[] { "Owner", "Manager", "Trainer", "Receptionist", "Member" })
        {
            if (!await _roleManager.RoleExistsAsync(roleName))
                await _roleManager.CreateAsync(new IdentityRole<Guid> { Name = roleName });
        }
    }

    private async Task<string?> ResolveUniqueGymCodeAsync(
        string? requested, string city, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var code = requested.Trim().ToUpperInvariant();
            var taken = await _db.Tenants.IgnoreQueryFilters()
                .AnyAsync(t => t.GymCode == code && !t.IsDeleted, ct);
            return taken ? null : code;
        }

        var citySlug = new string(city.Trim().ToUpperInvariant()
            .Where(char.IsLetterOrDigit)
            .Take(6)
            .ToArray());
        if (string.IsNullOrEmpty(citySlug))
            citySlug = "GYM";

        for (var attempt = 0; attempt < 12; attempt++)
        {
            var suffix = RandomNumberGenerator.GetInt32(1000, 9999);
            var code = $"GYM-{citySlug}-{suffix}";
            var taken = await _db.Tenants.IgnoreQueryFilters()
                .AnyAsync(t => t.GymCode == code && !t.IsDeleted, ct);
            if (!taken)
                return code;
        }

        return $"GYM-{Guid.NewGuid():N}"[..16].ToUpperInvariant();
    }

    private static string BuildDefaultSettingsJson()
    {
        var settings = new Dictionary<string, object?>
        {
            [TenantSettingsKeys.RequirePaperWaiver] = false,
            [TenantSettingsKeys.VatEnabled] = false,
            [TenantSettingsKeys.VatRate] = 0.14m,
            [TenantSettingsKeys.VarianceToleranceEgp] = 20m,
            [TenantSettingsKeys.AllowDowngrades] = true,
            [TenantSettingsKeys.AllowMidcycleChanges] = true,
            [TenantSettingsKeys.TransferFeeEgp] = 0m,
            [TenantSettingsKeys.InvoicePerPayment] = false
        };
        return JsonSerializer.Serialize(settings);
    }

    private void SeedDefaultPlans(Guid tenantId)
    {
        var now = DateTime.UtcNow;
        _db.MembershipPlans.AddRange(
            new MembershipPlan
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = "Monthly Unlimited",
                NameAr = "شهري غير محدود",
                Description = "Unlimited gym access for 30 days",
                DescriptionAr = "وصول غير محدود للصالة لمدة 30 يوم",
                PlanType = "monthly_unlimited",
                DurationDays = 30,
                Price = 500,
                Currency = "EGP",
                InvitationQuota = 2,
                ReferralInviteQuota = 5,
                IsActive = true,
                CreatedAtUtc = now
            },
            new MembershipPlan
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = "Session Pack 20",
                NameAr = "باقة 20 جلسة",
                Description = "20 fitness sessions valid for 90 days",
                DescriptionAr = "20 جلسة تدريب صحيحة لمدة 90 يوم",
                PlanType = "session_pack",
                DurationDays = 90,
                SessionCount = 20,
                Price = 800,
                Currency = "EGP",
                InvitationQuota = 1,
                ReferralInviteQuota = 15,
                IsActive = true,
                CreatedAtUtc = now
            },
            new MembershipPlan
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = "Morning Pass",
                NameAr = "تذكرة الصباح",
                Description = "Access 6 AM - 12 PM daily",
                DescriptionAr = "وصول من 6 صباحاً إلى 12 ظهراً يومياً",
                PlanType = "time_limited",
                DurationDays = 30,
                TimeRestrictionStart = TimeOnly.Parse("06:00"),
                TimeRestrictionEnd = TimeOnly.Parse("12:00"),
                Price = 300,
                Currency = "EGP",
                InvitationQuota = 0,
                IsActive = true,
                CreatedAtUtc = now
            });

        var floor = new Activity
        {
            TenantId = tenantId,
            Name = "Gym floor",
            NameAr = "صالة الجيم",
            Description = "Door check-in access",
            DescriptionAr = "دخول الصالة",
            Kind = ActivityKinds.Facility,
            SystemKey = ActivitySystemKeys.GymFloor,
            IsSystem = true,
            IsActive = true,
            BookingRequired = false,
            VisibleToMembers = false,
            CreatedAtUtc = now
        };
        _db.Activities.Add(floor);

        // Materialize first — adding PlanEntitlements mutates the change tracker and
        // must not run while enumerating MembershipPlans.Local.
        var plansForTenant = _db.MembershipPlans.Local
            .Where(p => p.TenantId == tenantId)
            .ToList();

        foreach (var plan in plansForTenant)
        {
            _db.PlanEntitlements.Add(new PlanEntitlement
            {
                TenantId = tenantId,
                PlanId = plan.Id,
                ActivityId = floor.Id,
                AccessMode = EntitlementAccessModes.Included,
                CreatedAtUtc = now
            });
        }
    }
}
