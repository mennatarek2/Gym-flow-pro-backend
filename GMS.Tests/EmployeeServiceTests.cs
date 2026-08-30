namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.DTOs.Admin;
using GMS.Application.DTOs.Hr;
using GMS.Application.Interfaces;
using GMS.Application.Services;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Services;

public class EmployeeServiceTests
{
    private sealed class NoOpAudit : IAuditService
    {
        public Task LogAsync(string action, string? entityType = null, Guid? entityId = null, object? before = null, object? after = null, Guid? tenantIdOverride = null)
            => Task.CompletedTask;

        public Task<GMS.Application.Common.Result<GMS.Application.Common.PagedResult<GMS.Application.DTOs.Audit.AuditEventDto>>> GetAuditEventsAsync(
            Guid tenantId, GMS.Application.DTOs.Audit.AuditEventQueryRequest query)
            => Task.FromResult(GMS.Application.Common.Result<GMS.Application.Common.PagedResult<GMS.Application.DTOs.Audit.AuditEventDto>>.Failure("n/a"));
    }

    private sealed class NoOpFileStorage : IFileStorageService
    {
        public Task<string> UploadAsync(Stream stream, string fileName, string folder) => Task.FromResult($"/uploads/{folder}/{fileName}");
        public Task DeleteAsync(string fileUrl) => Task.CompletedTask;
        public Task<bool> ExistsAsync(string fileUrl) => Task.FromResult(true);
    }

    /// <summary>In-memory stand-in for the real Staff/Identity join — lets LinkStaffAsync tests control
    /// exactly which staff accounts exist without standing up UserManager/Identity infrastructure.</summary>
    private sealed class FakeAdminService : IAdminService
    {
        public List<StaffListItemDto> Staff { get; } = new();

        public Task<GMS.Application.Common.Result<List<StaffListItemDto>>> GetStaffUsersAsync(Guid tenantId)
            => Task.FromResult(GMS.Application.Common.Result<List<StaffListItemDto>>.Success(Staff));

        public Task<GMS.Application.Common.Result<StaffDetailDto>> GetStaffUserByIdAsync(Guid tenantId, Guid id)
        {
            var s = Staff.FirstOrDefault(x => x.Id == id);
            if (s == null)
                return Task.FromResult(GMS.Application.Common.Result<StaffDetailDto>.Failure("not found"));
            return Task.FromResult(GMS.Application.Common.Result<StaffDetailDto>.Success(new StaffDetailDto
            {
                Id = s.Id,
                AppUserId = s.AppUserId,
                FullName = s.FullName,
                Email = s.Email,
                Role = s.Role,
                IsActive = s.IsActive,
                CreatedAtUtc = s.CreatedAtUtc
            }));
        }

        public Task<GMS.Application.Common.Result<StaffDetailDto>> CreateStaffUserAsync(Guid tenantId, GMS.Application.DTOs.Admin.CreateStaffRequest request)
            => throw new NotImplementedException();
        public Task<GMS.Application.Common.Result<StaffDetailDto>> UpdateStaffUserAsync(Guid tenantId, Guid id, GMS.Application.DTOs.Admin.UpdateStaffRequest request)
            => throw new NotImplementedException();
        public Task<GMS.Application.Common.Result> DeleteStaffUserAsync(Guid tenantId, Guid id)
            => throw new NotImplementedException();
        public Task<GMS.Application.Common.Result> ResetStaffPasswordAsync(Guid tenantId, Guid id, string newPassword)
            => throw new NotImplementedException();
        public Task<GMS.Application.Common.Result<StaffDetailDto>> SetStaffPhotoAsync(Guid tenantId, Guid id, Stream image, string fileName, string contentType)
            => throw new NotImplementedException();
        public Task<GMS.Application.Common.Result<List<GMS.Application.DTOs.Admin.StaffActivityItemDto>>> GetStaffActivityAsync(Guid tenantId, Guid id, int take = 30)
            => throw new NotImplementedException();
    }

    private static async Task<(GymFlowProDbContext ctx, EmployeeService svc, Guid tenantId)> SeedAsync()
    {
        var tenantId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var tenantContext = new TenantContext();
        tenantContext.SetTenant(tenantId, "Test Gym", "Africa/Cairo");
        var ctx = new GymFlowProDbContext(options, tenantContext);

        ctx.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Test Gym",
            NameAr = "صالة",
            GymCode = $"T-{tenantId:N}"[..12],
            City = "Cairo",
            Address = "x",
            PhoneNumber = "01000000000",
            Email = $"{tenantId:N}@test.local",
            SubscriptionStartDate = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var svc = new EmployeeService(ctx, new NoOpAudit(), new NoOpFileStorage(), NullLogger<EmployeeService>.Instance);
        return (ctx, svc, tenantId);
    }

    private static async Task<(GymFlowProDbContext ctx, EmployeeService svc, FakeAdminService admin, Guid tenantId, Guid appUserId)> SeedWithLinkableStaffAsync()
    {
        var (ctx, _, tenantId) = await SeedAsync();
        var identityId = Guid.NewGuid();
        var appUser = new AppUser
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = identityId.ToString(),
            FirstName = "Sara",
            LastName = "Trainer",
            Email = "sara@test.local",
            PhoneNumber = "0100000000",
            Role = "trainer",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.AppUsers.Add(appUser);
        await ctx.SaveChangesAsync();

        var admin = new FakeAdminService();
        admin.Staff.Add(new StaffListItemDto
        {
            Id = identityId,
            AppUserId = appUser.Id,
            FullName = "Sara Trainer",
            Email = "sara@test.local",
            Role = "Trainer",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        });

        var svc = new EmployeeService(ctx, new NoOpAudit(), new NoOpFileStorage(), NullLogger<EmployeeService>.Instance, admin);
        return (ctx, svc, admin, tenantId, appUser.Id);
    }

    private static CreateEmployeeRequest MakeRequest(string first = "Ahmed", string last = "Mohamed") => new()
    {
        FirstName = first,
        LastName = last,
        HireDate = new DateOnly(2026, 1, 1)
    };

    [Fact]
    public async Task CreateAsync_AllocatesSequentialEmployeeNumbers()
    {
        var (_, svc, tenantId) = await SeedAsync();

        var first = await svc.CreateAsync(tenantId, MakeRequest("A", "One"));
        var second = await svc.CreateAsync(tenantId, MakeRequest("B", "Two"));

        Assert.True(first.IsSuccess, first.Error);
        Assert.True(second.IsSuccess, second.Error);
        Assert.Equal("EMP-0001", first.Data!.EmployeeNumber);
        Assert.Equal("EMP-0002", second.Data!.EmployeeNumber);
    }

    [Fact]
    public async Task CreateAsync_NewEmployeeHasNoLoginByDefault()
    {
        var (_, svc, tenantId) = await SeedAsync();

        var result = await svc.CreateAsync(tenantId, MakeRequest());

        Assert.True(result.IsSuccess, result.Error);
        Assert.False(result.Data!.HasLogin);
        Assert.Null(result.Data.AppUserId);
    }

    [Fact]
    public async Task TerminateAsync_PreservesHistoryAndSetsStatus()
    {
        var (_, svc, tenantId) = await SeedAsync();
        var created = await svc.CreateAsync(tenantId, MakeRequest());

        var terminated = await svc.TerminateAsync(tenantId, created.Data!.Id, new TerminateEmployeeRequest
        {
            TerminationDate = new DateOnly(2026, 6, 1)
        });

        Assert.True(terminated.IsSuccess, terminated.Error);
        Assert.Equal(EmployeeStatuses.Terminated, terminated.Data!.Status);
        Assert.Equal(new DateOnly(2026, 6, 1), terminated.Data.TerminationDate);
        Assert.Equal(created.Data.EmployeeNumber, terminated.Data.EmployeeNumber);
    }

    [Fact]
    public async Task TerminateAsync_CannotTerminateTwice()
    {
        var (_, svc, tenantId) = await SeedAsync();
        var created = await svc.CreateAsync(tenantId, MakeRequest());
        await svc.TerminateAsync(tenantId, created.Data!.Id, new TerminateEmployeeRequest { TerminationDate = new DateOnly(2026, 6, 1) });

        var second = await svc.TerminateAsync(tenantId, created.Data.Id, new TerminateEmployeeRequest { TerminationDate = new DateOnly(2026, 7, 1) });

        Assert.False(second.IsSuccess);
    }

    [Fact]
    public async Task EmployeeNumbering_IsIsolatedPerTenant()
    {
        var (ctxA, svcA, tenantA) = await SeedAsync();

        var tenantB = Guid.NewGuid();
        var tenantContextB = new TenantContext();
        tenantContextB.SetTenant(tenantB, "Other Gym", "Africa/Cairo");
        var optionsB = new DbContextOptionsBuilder<GymFlowProDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var ctxB = new GymFlowProDbContext(optionsB, tenantContextB);
        ctxB.Tenants.Add(new Tenant
        {
            Id = tenantB,
            Name = "Other Gym",
            NameAr = "صالة أخرى",
            GymCode = $"T-{tenantB:N}"[..12],
            City = "Cairo",
            Address = "x",
            PhoneNumber = "01000000001",
            Email = $"{tenantB:N}@test.local",
            SubscriptionStartDate = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow
        });
        await ctxB.SaveChangesAsync();
        var svcB = new EmployeeService(ctxB, new NoOpAudit(), new NoOpFileStorage(), NullLogger<EmployeeService>.Instance);

        var employeeA = await svcA.CreateAsync(tenantA, MakeRequest("A", "One"));
        var employeeB = await svcB.CreateAsync(tenantB, MakeRequest("B", "One"));

        Assert.Equal("EMP-0001", employeeA.Data!.EmployeeNumber);
        Assert.Equal("EMP-0001", employeeB.Data!.EmployeeNumber);

        var listA = await svcA.ListAsync(tenantA);
        Assert.Single(listA.Data!);
        Assert.DoesNotContain(listA.Data!, e => e.Id == employeeB.Data.Id);
    }

    [Fact]
    public async Task CreateAsync_RejectsAppUserAlreadyLinkedToAnotherEmployee()
    {
        var (ctx, svc, tenantId) = await SeedAsync();
        var appUser = new AppUser
        {
            TenantId = tenantId,
            UserId = Guid.NewGuid().ToString(),
            FirstName = "Staff",
            LastName = "One",
            Email = "staff@test.local",
            Role = "Trainer",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.AppUsers.Add(appUser);
        await ctx.SaveChangesAsync();

        var request = MakeRequest();
        request.AppUserId = appUser.Id;
        var first = await svc.CreateAsync(tenantId, request);
        Assert.True(first.IsSuccess, first.Error);

        var secondRequest = MakeRequest("Second", "Employee");
        secondRequest.AppUserId = appUser.Id;
        var second = await svc.CreateAsync(tenantId, secondRequest);

        Assert.False(second.IsSuccess);
    }

    [Fact]
    public async Task LinkStaffAsync_SetsAppUserIdAndPopulatesStaffAccount()
    {
        var (_, svc, _, tenantId, appUserId) = await SeedWithLinkableStaffAsync();
        var emp = await svc.CreateAsync(tenantId, MakeRequest());

        var result = await svc.LinkStaffAsync(tenantId, emp.Data!.Id, appUserId);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(appUserId, result.Data!.AppUserId);
        Assert.True(result.Data.HasLogin);
        Assert.NotNull(result.Data.StaffAccount);
        Assert.Equal("Sara Trainer", result.Data.StaffAccount!.FullName);
        Assert.Equal("Trainer", result.Data.StaffAccount.Role);
        Assert.Equal("Active", result.Data.StaffAccount.Status);
    }

    [Fact]
    public async Task LinkStaffAsync_RejectsAppUserAlreadyLinkedToAnotherEmployee()
    {
        var (_, svc, _, tenantId, appUserId) = await SeedWithLinkableStaffAsync();
        var emp1 = await svc.CreateAsync(tenantId, MakeRequest("A", "One"));
        var emp2 = await svc.CreateAsync(tenantId, MakeRequest("B", "Two"));
        var link1 = await svc.LinkStaffAsync(tenantId, emp1.Data!.Id, appUserId);
        Assert.True(link1.IsSuccess, link1.Error);

        var link2 = await svc.LinkStaffAsync(tenantId, emp2.Data!.Id, appUserId);

        Assert.False(link2.IsSuccess);
    }

    [Fact]
    public async Task LinkStaffAsync_RejectsUnknownAppUserId()
    {
        var (_, svc, _, tenantId, _) = await SeedWithLinkableStaffAsync();
        var emp = await svc.CreateAsync(tenantId, MakeRequest());

        var result = await svc.LinkStaffAsync(tenantId, emp.Data!.Id, Guid.NewGuid());

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task UnlinkStaffAsync_ClearsAppUserIdAndStaffAccount()
    {
        var (_, svc, _, tenantId, appUserId) = await SeedWithLinkableStaffAsync();
        var emp = await svc.CreateAsync(tenantId, MakeRequest());
        await svc.LinkStaffAsync(tenantId, emp.Data!.Id, appUserId);

        var result = await svc.UnlinkStaffAsync(tenantId, emp.Data!.Id);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Null(result.Data!.AppUserId);
        Assert.False(result.Data.HasLogin);
        Assert.Null(result.Data.StaffAccount);
    }

    [Fact]
    public async Task UnlinkStaffAsync_RejectsWhenNotLinked()
    {
        var (_, svc, _, tenantId, _) = await SeedWithLinkableStaffAsync();
        var emp = await svc.CreateAsync(tenantId, MakeRequest());

        var result = await svc.UnlinkStaffAsync(tenantId, emp.Data!.Id);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task ListAvailableStaffAsync_ExcludesAccountsLinkedToAnotherEmployee()
    {
        var (_, svc, _, tenantId, appUserId) = await SeedWithLinkableStaffAsync();
        var emp1 = await svc.CreateAsync(tenantId, MakeRequest("A", "One"));
        var emp2 = await svc.CreateAsync(tenantId, MakeRequest("B", "Two"));
        await svc.LinkStaffAsync(tenantId, emp1.Data!.Id, appUserId);

        var availableForEmp2 = await svc.ListAvailableStaffAsync(tenantId, emp2.Data!.Id);

        Assert.True(availableForEmp2.IsSuccess, availableForEmp2.Error);
        Assert.Empty(availableForEmp2.Data!);
    }

    [Fact]
    public async Task ListAvailableStaffAsync_ReturnsUnlinkedStaff()
    {
        var (_, svc, _, tenantId, appUserId) = await SeedWithLinkableStaffAsync();
        var emp = await svc.CreateAsync(tenantId, MakeRequest());

        var result = await svc.ListAvailableStaffAsync(tenantId, emp.Data!.Id);

        Assert.True(result.IsSuccess, result.Error);
        var row = Assert.Single(result.Data!);
        Assert.Equal(appUserId, row.AppUserId);
        Assert.Equal("Sara Trainer", row.FullName);
    }

    [Fact]
    public async Task UnlinkStaffAsync_DoesNotClearEmployeeAppUserId()
    {
        var (ctx, svc, _, tenantId, appUserId) = await SeedWithLinkableStaffAsync();
        var emp = await svc.CreateAsync(tenantId, MakeRequest());
        await svc.LinkStaffAsync(tenantId, emp.Data!.Id, appUserId);

        var employeeAppUser = new AppUser
        {
            TenantId = tenantId,
            UserId = Guid.NewGuid().ToString(),
            FirstName = "Ahmed",
            LastName = "App",
            Email = "emp0001@employee.gymflowpro.local",
            Role = "Employee",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.AppUsers.Add(employeeAppUser);
        await ctx.SaveChangesAsync();

        var entity = await ctx.Employees.FirstAsync(e => e.Id == emp.Data!.Id);
        entity.EmployeeAppUserId = employeeAppUser.Id;
        await ctx.SaveChangesAsync();

        var result = await svc.UnlinkStaffAsync(tenantId, emp.Data!.Id);

        Assert.True(result.IsSuccess, result.Error);
        Assert.Null(result.Data!.AppUserId);
        var reloaded = await ctx.Employees.AsNoTracking().FirstAsync(e => e.Id == emp.Data.Id);
        Assert.Equal(employeeAppUser.Id, reloaded.EmployeeAppUserId);
    }

    [Fact]
    public async Task ResolveEmployeeIdForCallerAsync_UsesEmployeeAppUserId_WhenActive()
    {
        var (ctx, svc, tenantId) = await SeedAsync();
        var identityId = Guid.NewGuid();
        var appUser = new AppUser
        {
            TenantId = tenantId,
            UserId = identityId.ToString(),
            FirstName = "Emp",
            LastName = "App",
            Email = "e@employee.gymflowpro.local",
            Role = "Employee",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.AppUsers.Add(appUser);
        await ctx.SaveChangesAsync();

        var created = await svc.CreateAsync(tenantId, MakeRequest());
        var entity = await ctx.Employees.FirstAsync(e => e.Id == created.Data!.Id);
        entity.EmployeeAppUserId = appUser.Id;
        await ctx.SaveChangesAsync();

        var resolved = await svc.ResolveEmployeeIdForCallerAsync(tenantId, identityId);
        Assert.Equal(created.Data.Id, resolved);
    }

    [Fact]
    public async Task ResolveEmployeeIdForCallerAsync_ReturnsNull_WhenSuspended()
    {
        var (ctx, svc, tenantId) = await SeedAsync();
        var identityId = Guid.NewGuid();
        var appUser = new AppUser
        {
            TenantId = tenantId,
            UserId = identityId.ToString(),
            FirstName = "Emp",
            LastName = "App",
            Email = "e2@employee.gymflowpro.local",
            Role = "Employee",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.AppUsers.Add(appUser);
        await ctx.SaveChangesAsync();

        var created = await svc.CreateAsync(tenantId, MakeRequest());
        var entity = await ctx.Employees.FirstAsync(e => e.Id == created.Data!.Id);
        entity.EmployeeAppUserId = appUser.Id;
        entity.Status = EmployeeStatuses.Suspended;
        await ctx.SaveChangesAsync();

        var resolved = await svc.ResolveEmployeeIdForCallerAsync(tenantId, identityId);
        Assert.Null(resolved);
    }

    [Fact]
    public async Task GetMeAsync_ReturnsProfile_ForEmployeeAppIdentity()
    {
        var (ctx, svc, tenantId) = await SeedAsync();
        var identityId = Guid.NewGuid();
        var appUser = new AppUser
        {
            TenantId = tenantId,
            UserId = identityId.ToString(),
            FirstName = "Emp",
            LastName = "App",
            Email = "e3@employee.gymflowpro.local",
            Role = "Employee",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.AppUsers.Add(appUser);
        await ctx.SaveChangesAsync();

        var created = await svc.CreateAsync(tenantId, MakeRequest("Sara", "Cleaner"));
        var entity = await ctx.Employees.FirstAsync(e => e.Id == created.Data!.Id);
        entity.EmployeeAppUserId = appUser.Id;
        await ctx.SaveChangesAsync();

        var me = await svc.GetMeAsync(tenantId, identityId);
        Assert.True(me.IsSuccess, me.Error);
        Assert.Equal(created.Data.Id, me.Data!.Id);
        Assert.Equal("Sara Cleaner", me.Data.FullName);
        Assert.Equal(created.Data.EmployeeNumber, me.Data.EmployeeNumber);
    }

    [Fact]
    public async Task LinkStaffAsync_RejectsEmployeeRoleAppUser()
    {
        var (ctx, svc, tenantId) = await SeedAsync();
        var empAppUser = new AppUser
        {
            TenantId = tenantId,
            UserId = Guid.NewGuid().ToString(),
            FirstName = "Emp",
            LastName = "Only",
            Email = "only@employee.gymflowpro.local",
            Role = "Employee",
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.AppUsers.Add(empAppUser);
        await ctx.SaveChangesAsync();

        var emp = await svc.CreateAsync(tenantId, MakeRequest());
        var result = await svc.LinkStaffAsync(tenantId, emp.Data!.Id, empAppUser.Id);
        Assert.False(result.IsSuccess);
    }
}
