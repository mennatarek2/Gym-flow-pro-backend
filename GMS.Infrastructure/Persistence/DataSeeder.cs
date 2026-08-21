namespace GMS.Infrastructure.Persistence;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using GMS.Core.Entities;
using GMS.Core.Entities.Identity;

/// <summary>
/// Database seeder for initial test data.
/// Seeds roles, tenant, users, membership plans, and sample member.
/// Only runs once — exits if any tenants exist (idempotency).
/// </summary>
public class DataSeeder
{
    private readonly GymFlowProDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;

    public DataSeeder(
        GymFlowProDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _roleManager = roleManager;
    }

    /// <summary>
    /// Seeds all test data. Idempotent — only runs if database is empty.
    /// Returns the newly created tenant id when seed ran; null when skipped.
    /// </summary>
    public async Task<Guid?> SeedAsync()
    {
        // Identity roles must exist before member OTP can assign "Member" (runs even when tenants already exist).
        await EnsureIdentityRolesAsync();

        // Idempotency check: if any tenants exist, don't seed again
        if (await _dbContext.Tenants.AnyAsync())
            return null;

        Console.WriteLine("🌱 Starting database seed...");

        // Step 1: Seed tenant
        var tenant = await SeedTenantAsync();
        Console.WriteLine("✅ Tenant seeded");

        // Step 2: Seed users with roles
        var owner = await SeedUsersAsync(tenant.Id);
        Console.WriteLine("✅ Users seeded");

        // Step 3: Seed membership plans
        var monthlyPlan = await SeedMembershipPlansAsync(tenant.Id);
        Console.WriteLine("✅ Membership plans seeded");

        // Step 4: Seed sample member with active membership
        await SeedSampleMemberAsync(tenant.Id, monthlyPlan.Id);
        Console.WriteLine("✅ Sample member seeded");

        await _dbContext.SaveChangesAsync();
        Console.WriteLine("🎉 Database seed completed successfully!");
        Console.WriteLine("\n📝 Test Credentials:");
        Console.WriteLine("   Owner:        owner@gymflow.test / Test@1234");
        Console.WriteLine("   Manager:      manager@gymflow.test / Test@1234");
        Console.WriteLine("   Trainer:      trainer@gymflow.test / Test@1234");
        Console.WriteLine("   Receptionist: receptionist@gymflow.test / Test@1234");
        Console.WriteLine("   Gym Code: GYM-TEST-01\n");

        return tenant.Id;
    }

    /// <summary>
    /// Ensures Identity roles exist (idempotent). Call on every startup so <c>Member</c> exists for OTP sign-in.
    /// </summary>
    public async Task EnsureIdentityRolesAsync()
    {
        var roles = new[] { "Owner", "Manager", "Trainer", "Receptionist", "Member" };

        foreach (var roleName in roles)
        {
            var roleExists = await _roleManager.RoleExistsAsync(roleName);
            if (!roleExists)
            {
                await _roleManager.CreateAsync(new IdentityRole<Guid> { Name = roleName });
            }
        }
    }

    /// <summary>
    /// Seeds the test tenant (Iron Zone Gym).
    /// </summary>
    private async Task<Tenant> SeedTenantAsync()
    {
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Iron Zone Gym",
            NameAr = "صالة حديد زون",
            GymCode = "GYM-TEST-01",
            City = "Cairo",
            Address = "123 Fitness Street",
            PhoneNumber = "+201000000000",
            Email = "info@ironzone.test",
            IsActive = true,
            SubscriptionStartDate = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        };

        await _dbContext.Tenants.AddAsync(tenant);
        return tenant;
    }

    /// <summary>
    /// Seeds three test users (Owner, Manager, Trainer) with proper roles.
    /// Uses FIXED GUIDs so tokens remain consistent across database resets.
    /// Uses UserManager.CreateAsync to ensure passwords are hashed correctly.
    /// </summary>
    private async Task<ApplicationUser> SeedUsersAsync(Guid tenantId)
    {
        ApplicationUser ownerUser = null;

        // Fixed GUIDs for consistent token generation across database resets
        var userIds = new Dictionary<string, Guid>
        {
            ["owner@gymflow.test"] = Guid.Parse("f8677f57-8b74-46a1-9698-08deadd7eaba"),
            ["manager@gymflow.test"] = Guid.Parse("12345678-1234-1234-1234-123456789012"),
            ["trainer@gymflow.test"] = Guid.Parse("87654321-4321-4321-4321-210987654321"),
            ["receptionist@gymflow.test"] = Guid.Parse("a1b2c3d4-e5f6-4a1b-9c2d-3e4f5a6b7c8d")
        };

        var users = new[]
        {
            new { Email = "owner@gymflow.test", FullName = "Ahmed Owner", Role = "Owner" },
            new { Email = "manager@gymflow.test", FullName = "Sara Manager", Role = "Manager" },
            new { Email = "trainer@gymflow.test", FullName = "Omar Trainer", Role = "Trainer" },
            new { Email = "receptionist@gymflow.test", FullName = "Laila Reception", Role = "Receptionist" }
        };

        foreach (var userData in users)
        {
            var userExists = await _userManager.FindByEmailAsync(userData.Email);
            if (userExists != null)
                continue;

            var nameParts = userData.FullName.Split(' ');
            var identityUser = new ApplicationUser
            {
                Id = userIds[userData.Email],  // Use fixed GUID instead of auto-generated
                UserName = userData.Email,
                Email = userData.Email,
                EmailConfirmed = true,
                FirstName = nameParts.Length > 0 ? nameParts[0] : userData.FullName,
                LastName = nameParts.Length > 1 ? nameParts[1] : string.Empty,
                TenantId = tenantId
            };

            // Create user with password (UserManager hashes it automatically)
            var result = await _userManager.CreateAsync(identityUser, "Test@1234");
            if (!result.Succeeded)
                throw new Exception($"Failed to create user {userData.Email}: {string.Join(", ", result.Errors.Select(e => e.Description))}");

            // Assign role
            var roleResult = await _userManager.AddToRoleAsync(identityUser, userData.Role);
            if (!roleResult.Succeeded)
                throw new Exception($"Failed to assign role to user {userData.Email}");

            // CRITICAL: Also create the domain AppUser row.
            // The manual check-in service looks up staff via AppUser.UserId == ApplicationUser.Id.
            // Without this row the lookup always fails with "Staff user not found".
            var staffIndex = Array.FindIndex(users, u => u.Email == userData.Email) + 1;
            var appUser = new AppUser
            {
                TenantId = tenantId,
                UserId    = identityUser.Id.ToString(),   // FK link to AspNetUsers
                FirstName = identityUser.FirstName,
                LastName  = identityUser.LastName,
                Email     = identityUser.Email!,
                Role      = userData.Role,
                IsActive  = true,
                CreatedAtUtc = DateTime.UtcNow,
                StaffNumber = $"ST-{staffIndex:D4}"
            };
            await _dbContext.AppUsers.AddAsync(appUser);

            if (userData.Role == "Owner")
                ownerUser = identityUser;
        }

        return ownerUser;
    }

    /// <summary>
    /// Seeds three test membership plans.
    /// </summary>
    private async Task<MembershipPlan> SeedMembershipPlansAsync(Guid tenantId)
    {
        var monthlyPlan = new MembershipPlan
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
            CreatedAtUtc = DateTime.UtcNow
        };

        var sessionPlan = new MembershipPlan
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
            CreatedAtUtc = DateTime.UtcNow
        };

        var morningPlan = new MembershipPlan
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
            ReferralInviteQuota = 0,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        await _dbContext.MembershipPlans.AddAsync(monthlyPlan);
        await _dbContext.MembershipPlans.AddAsync(sessionPlan);
        await _dbContext.MembershipPlans.AddAsync(morningPlan);

        return monthlyPlan;
    }

    /// <summary>
    /// Seeds a sample member (Karim) with an active membership.
    /// </summary>
    private async Task SeedSampleMemberAsync(Guid tenantId, Guid planId)
    {
        var member = new GymMember
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FullName = "Karim Member",
            FullNameAr = "كريم",
            PhoneNumber = "+201234567890",
            Email = "karim@gymflow.test",
            DateOfBirth = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-25)),
            MemberNumber = "GYM-001",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        await _dbContext.GymMembers.AddAsync(member);
        
        // Create active membership
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var membership = new Membership
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            MemberId = member.Id,
            PlanId = planId,
            StartDate = today,
            EndDate = today.AddDays(30),
            Status = "active",
            PaymentMethod = "cash",
            AmountPaid = 500,
            PaymentDate = DateTime.UtcNow,
            AutoRenew = false,
            CreatedAtUtc = DateTime.UtcNow
        };

        await _dbContext.Memberships.AddAsync(membership);
    }

    /// <summary>
    /// Idempotent demo offers for GYM-TEST-01 so Member App GET /api/member/offers has data.
    /// Development only. Skips when the tenant already has any offer rows.
    /// </summary>
    public async Task EnsureDemoOffersAsync()
    {
        var tenant = await _dbContext.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.GymCode == "GYM-TEST-01");
        if (tenant == null)
            return;

        var hasOffers = await _dbContext.Offers
            .IgnoreQueryFilters()
            .AnyAsync(o => o.TenantId == tenant.Id && !o.IsDeleted);
        if (hasOffers)
            return;

        async Task<PromoCode> EnsurePromo(string code, string type, decimal value, DateOnly from, DateOnly to, bool active)
        {
            var existing = await _dbContext.PromoCodes.IgnoreQueryFilters()
                .FirstOrDefaultAsync(p => p.TenantId == tenant.Id && p.Code == code && !p.IsDeleted);
            if (existing != null)
                return existing;

            var promo = new PromoCode
            {
                TenantId = tenant.Id,
                Code = code,
                Type = type,
                Value = value,
                ValidFrom = from,
                ValidTo = to,
                MaxUses = 100,
                MaxUsesPerMember = 1,
                IsActive = active
            };
            await _dbContext.PromoCodes.AddAsync(promo);
            return promo;
        }

        var summerPromo = await EnsurePromo(
            "SUMMER20", "percent", 20,
            new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 15), true);
        var merchPromo = await EnsurePromo(
            "MERCH10", "percent", 10,
            new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), false);

        _dbContext.Offers.AddRange(
            new Offer
            {
                TenantId = tenant.Id,
                Name = "Summer Membership Offer",
                NameAr = "عرض عضوية الصيف",
                ShortDescription = "Save 20% on selected membership plans this summer.",
                ShortDescriptionAr = "وفّر ٢٠٪ على خطط الاشتراك المختارة هذا الصيف.",
                Description = "Limited summer campaign.",
                StartDate = new DateOnly(2026, 8, 1),
                EndDate = new DateOnly(2026, 9, 15),
                AppliesTo = "memberships",
                MembershipLabelsJson = "[\"3 Months\"]",
                DiscountType = "percentage",
                Value = 20,
                AllMembers = true,
                UsageLimit = 100,
                PerMemberLimit = 1,
                UsesCount = 12,
                ShowOnMemberApp = true,
                Featured = true,
                ShowBanner = true,
                DisplayOrder = 1,
                Redemption = "promoCode",
                PromoCode = "SUMMER20",
                PromoCodeId = summerPromo.Id,
                IsDraft = false
            },
            new Offer
            {
                TenantId = tenant.Id,
                Name = "Protein Bundle",
                NameAr = "باقة البروتين",
                ShortDescription = "Protein Bars promotion.",
                ShortDescriptionAr = "عرض ألواح البروتين.",
                Description = "Buy 2 get 1 free on selected bars.",
                StartDate = new DateOnly(2026, 8, 1),
                EndDate = new DateOnly(2026, 9, 10),
                AppliesTo = "products",
                ProductLabelsJson = "[\"Protein Bar\"]",
                DiscountType = "bxgy",
                BuyQty = 2,
                GetQty = 1,
                AllMembers = true,
                UsageLimit = 200,
                PerMemberLimit = 2,
                ShowOnMemberApp = true,
                Featured = false,
                ShowBanner = true,
                DisplayOrder = 2,
                Redemption = "automatic",
                IsDraft = false
            },
            new Offer
            {
                TenantId = tenant.Id,
                Name = "New Member Welcome",
                NameAr = "ترحيب العضو الجديد",
                ShortDescription = "Automatic welcome discount.",
                ShortDescriptionAr = "خصم ترحيبي تلقائي.",
                Description = "First membership purchase only.",
                StartDate = new DateOnly(2026, 1, 1),
                EndDate = new DateOnly(2026, 12, 31),
                AppliesTo = "memberships",
                DiscountType = "percentage",
                Value = 15,
                AllMembers = false,
                NewMembersOnly = true,
                PerMemberLimit = 1,
                UsesCount = 48,
                ShowOnMemberApp = false,
                Featured = false,
                ShowBanner = false,
                DisplayOrder = 3,
                Redemption = "automatic",
                IsDraft = false
            },
            new Offer
            {
                TenantId = tenant.Id,
                Name = "Gym Merchandise Sale",
                NameAr = "تخفيضات الملابس",
                ShortDescription = "Retail apparel sale.",
                ShortDescriptionAr = "تخفيضات ملابس الصالة.",
                Description = "Ended merch campaign.",
                StartDate = new DateOnly(2026, 7, 1),
                EndDate = new DateOnly(2026, 7, 31),
                AppliesTo = "products",
                DiscountType = "percentage",
                Value = 10,
                AllMembers = true,
                UsageLimit = 90,
                PerMemberLimit = 3,
                UsesCount = 90,
                ShowOnMemberApp = true,
                Featured = false,
                ShowBanner = true,
                DisplayOrder = 4,
                Redemption = "promoCode",
                PromoCode = "MERCH10",
                PromoCodeId = merchPromo.Id,
                IsDraft = false
            });

        await _dbContext.SaveChangesAsync();
        Console.WriteLine("✅ Demo offers seeded for GYM-TEST-01");
    }
}
