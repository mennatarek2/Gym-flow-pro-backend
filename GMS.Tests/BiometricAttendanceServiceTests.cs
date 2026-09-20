namespace GMS.Tests;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using GMS.Application.DTOs.Hr;
using GMS.Application.Options;
using GMS.Application.Services;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Utilities;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Services;

public class BiometricAttendanceServiceTests
{
    private sealed class NoOpAudit : GMS.Application.Interfaces.IAuditService
    {
        public Task LogAsync(string action, string? entityType = null, Guid? entityId = null, object? before = null, object? after = null, Guid? tenantIdOverride = null)
            => Task.CompletedTask;

        public Task<GMS.Application.Common.Result<GMS.Application.Common.PagedResult<GMS.Application.DTOs.Audit.AuditEventDto>>> GetAuditEventsAsync(
            Guid tenantId, GMS.Application.DTOs.Audit.AuditEventQueryRequest query)
            => Task.FromResult(GMS.Application.Common.Result<GMS.Application.Common.PagedResult<GMS.Application.DTOs.Audit.AuditEventDto>>.Failure("n/a"));
    }

    private static async Task<(GymFlowProDbContext ctx, BiometricAttendanceService svc, Guid tenantId, Guid employeeId, BiometricDevice device, string apiKey)> SeedAsync(
        int skewMinutes = 15, bool withOvernight = false)
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

        var employee = new Employee
        {
            TenantId = tenantId,
            EmployeeNumber = "EMP-0001",
            FirstName = "Sara",
            LastName = "Hassan",
            HireDate = new DateOnly(2026, 1, 1),
            Status = EmployeeStatuses.Active,
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Employees.Add(employee);

        var today = MembershipOperational.TodayCairo();
        if (withOvernight)
        {
            var shift = new EmployeeShift
            {
                TenantId = tenantId,
                Name = "Night",
                StartTime = new TimeOnly(22, 0),
                EndTime = new TimeOnly(6, 0),
                GraceMinutes = 10,
                CreatedAtUtc = DateTime.UtcNow
            };
            ctx.EmployeeShifts.Add(shift);
            ctx.EmployeeScheduleAssignments.Add(new EmployeeScheduleAssignment
            {
                TenantId = tenantId,
                EmployeeId = employee.Id,
                EmployeeShiftId = shift.Id,
                Date = today,
                CreatedAtUtc = DateTime.UtcNow
            });
        }
        else
        {
            var shift = new EmployeeShift
            {
                TenantId = tenantId,
                Name = "Morning",
                StartTime = new TimeOnly(9, 0),
                EndTime = new TimeOnly(17, 0),
                GraceMinutes = 10,
                CreatedAtUtc = DateTime.UtcNow
            };
            ctx.EmployeeShifts.Add(shift);
            ctx.EmployeeScheduleAssignments.Add(new EmployeeScheduleAssignment
            {
                TenantId = tenantId,
                EmployeeId = employee.Id,
                EmployeeShiftId = shift.Id,
                Date = today,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        await ctx.SaveChangesAsync();

        var opts = Options.Create(new BiometricAttendanceOptions { ProvisionalMaxClockSkewMinutes = skewMinutes });
        var svc = new BiometricAttendanceService(ctx, new NoOpAudit(), tenantContext, opts, NullLogger<BiometricAttendanceService>.Instance);

        var created = await svc.CreateAsync(tenantId, new CreateBiometricDeviceRequest
        {
            DeviceCode = "BIO-1",
            DisplayName = "Front",
            IntegrationType = BiometricIntegrationTypes.PushToLocal
        }, null);
        Assert.True(created.IsSuccess);
        var device = await ctx.BiometricDevices.FirstAsync();
        var apiKey = created.Data!.ApiKeyPlaintext;

        await svc.UpsertAsync(tenantId, new UpsertBiometricMappingRequest
        {
            BiometricDeviceId = device.Id,
            EmployeeId = employee.Id,
            DeviceUserId = "1001"
        });

        return (ctx, svc, tenantId, employee.Id, device, apiKey);
    }

    [Fact]
    public async Task Ingest_ValidFirstPunch_AppliesCheckIn()
    {
        var (ctx, svc, tenantId, employeeId, device, apiKey) = await SeedAsync();
        var now = DateTime.UtcNow;

        var result = await svc.IngestPushAsync(device.Id, apiKey, new BiometricPushBatchRequest
        {
            Events =
            {
                new BiometricPushEventRequest
                {
                    VendorEventId = "V-1",
                    DeviceUserId = "1001",
                    DeviceTimestamp = now.ToString("o")
                }
            }
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Data!.Applied);
        var att = await ctx.EmployeeAttendances.SingleAsync(a => a.EmployeeId == employeeId);
        Assert.Equal(AttendanceSources.Device, att.Source);
        Assert.NotNull(att.CheckInAtUtc);
        Assert.Null(att.CheckOutAtUtc);
    }

    [Fact]
    public async Task Ingest_SecondLaterPunch_AppliesCheckOut()
    {
        var (ctx, svc, _, employeeId, device, apiKey) = await SeedAsync(skewMinutes: 60);
        var t1 = DateTime.UtcNow.AddMinutes(-40);
        var t2 = DateTime.UtcNow.AddMinutes(-5);

        await svc.IngestPushAsync(device.Id, apiKey, new BiometricPushBatchRequest
        {
            Events = { new BiometricPushEventRequest { VendorEventId = "A", DeviceUserId = "1001", DeviceTimestamp = t1.ToString("o") } }
        });
        var second = await svc.IngestPushAsync(device.Id, apiKey, new BiometricPushBatchRequest
        {
            Events = { new BiometricPushEventRequest { VendorEventId = "B", DeviceUserId = "1001", DeviceTimestamp = t2.ToString("o") } }
        });

        Assert.True(second.IsSuccess);
        Assert.Equal(1, second.Data!.Applied);
        var att = await ctx.EmployeeAttendances.SingleAsync(a => a.EmployeeId == employeeId);
        Assert.NotNull(att.CheckOutAtUtc);
        Assert.True(att.WorkedMinutes > 0);
    }

    [Fact]
    public async Task Ingest_DuplicateVendorEventId_IsIdempotent()
    {
        var (ctx, svc, _, _, device, apiKey) = await SeedAsync();
        var ts = DateTime.UtcNow.ToString("o");
        var batch = new BiometricPushBatchRequest
        {
            Events = { new BiometricPushEventRequest { VendorEventId = "DUP-1", DeviceUserId = "1001", DeviceTimestamp = ts } }
        };
        await svc.IngestPushAsync(device.Id, apiKey, batch);
        var again = await svc.IngestPushAsync(device.Id, apiKey, batch);

        Assert.Equal(1, again.Data!.Duplicates);
        Assert.Equal(1, await ctx.EmployeeAttendances.CountAsync());
        Assert.Equal(1, await ctx.BiometricAttendanceEvents.CountAsync());
    }

    [Fact]
    public async Task Ingest_UnknownDevice_Fails()
    {
        var (_, svc, _, _, _, _) = await SeedAsync();
        var result = await svc.IngestPushAsync(Guid.NewGuid(), "hmbio_bad", new BiometricPushBatchRequest
        {
            Events = { new BiometricPushEventRequest { DeviceUserId = "1", DeviceTimestamp = DateTime.UtcNow.ToString("o") } }
        });
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task Ingest_InvalidApiKey_Fails()
    {
        var (_, svc, _, _, device, _) = await SeedAsync();
        var result = await svc.IngestPushAsync(device.Id, "wrong-key", new BiometricPushBatchRequest
        {
            Events = { new BiometricPushEventRequest { DeviceUserId = "1001", DeviceTimestamp = DateTime.UtcNow.ToString("o") } }
        });
        Assert.False(result.IsSuccess);
        Assert.Contains("credentials", result.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Ingest_UnmappedUser_NeedsReview()
    {
        var (ctx, svc, _, _, device, apiKey) = await SeedAsync();
        var result = await svc.IngestPushAsync(device.Id, apiKey, new BiometricPushBatchRequest
        {
            Events = { new BiometricPushEventRequest { VendorEventId = "U1", DeviceUserId = "9999", DeviceTimestamp = DateTime.UtcNow.ToString("o") } }
        });
        Assert.Equal(1, result.Data!.NeedsReview);
        Assert.Equal(0, await ctx.EmployeeAttendances.CountAsync());
        var ev = await ctx.BiometricAttendanceEvents.SingleAsync();
        Assert.Equal(BiometricEventStatuses.NeedsReview, ev.ProcessingStatus);
        Assert.Equal("UnmappedDeviceUser", ev.ValidationResult);
    }

    [Fact]
    public async Task Ingest_ClockSkew_NeedsReview_DoesNotWriteAttendance()
    {
        var (ctx, svc, _, _, device, apiKey) = await SeedAsync(skewMinutes: 5);
        var skewed = DateTime.UtcNow.AddMinutes(-30);
        var result = await svc.IngestPushAsync(device.Id, apiKey, new BiometricPushBatchRequest
        {
            Events = { new BiometricPushEventRequest { VendorEventId = "S1", DeviceUserId = "1001", DeviceTimestamp = skewed.ToString("o") } }
        });
        Assert.Equal(1, result.Data!.NeedsReview);
        Assert.Equal(0, await ctx.EmployeeAttendances.CountAsync());
    }

    [Fact]
    public async Task Ingest_MissingVendorEventId_FallbackDedupe()
    {
        var (ctx, svc, _, _, device, apiKey) = await SeedAsync();
        var ts = DateTime.UtcNow.ToString("o");
        var batch = new BiometricPushBatchRequest
        {
            Events = { new BiometricPushEventRequest { DeviceUserId = "1001", DeviceTimestamp = ts } }
        };
        await svc.IngestPushAsync(device.Id, apiKey, batch);
        var again = await svc.IngestPushAsync(device.Id, apiKey, batch);
        Assert.Equal(1, again.Data!.Duplicates);
        Assert.Equal(1, await ctx.BiometricAttendanceEvents.CountAsync());
    }

    [Fact]
    public async Task Ingest_DisabledEmployee_NeedsReview()
    {
        var (ctx, svc, tenantId, employeeId, device, apiKey) = await SeedAsync();
        var emp = await ctx.Employees.FirstAsync(e => e.Id == employeeId);
        emp.Status = EmployeeStatuses.Suspended;
        await ctx.SaveChangesAsync();

        // Mapping still enabled — attribution must still block on HR status.
        var result = await svc.IngestPushAsync(device.Id, apiKey, new BiometricPushBatchRequest
        {
            Events = { new BiometricPushEventRequest { VendorEventId = "X", DeviceUserId = "1001", DeviceTimestamp = DateTime.UtcNow.ToString("o") } }
        });
        Assert.Equal(1, result.Data!.NeedsReview);
        Assert.Equal("EmployeeIneligible", (await ctx.BiometricAttendanceEvents.SingleAsync()).ValidationResult);
    }

    [Fact]
    public async Task DisableMappings_BlocksAutoApply()
    {
        var (ctx, svc, tenantId, employeeId, device, apiKey) = await SeedAsync();
        await svc.DisableMappingsForEmployeeAsync(tenantId, employeeId, "terminated");
        var result = await svc.IngestPushAsync(device.Id, apiKey, new BiometricPushBatchRequest
        {
            Events = { new BiometricPushEventRequest { VendorEventId = "D1", DeviceUserId = "1001", DeviceTimestamp = DateTime.UtcNow.ToString("o") } }
        });
        Assert.Equal(1, result.Data!.NeedsReview);
        Assert.Equal(0, await ctx.EmployeeAttendances.CountAsync());
    }

    [Fact]
    public async Task ExistingQrCheckIn_EarlierDevicePunch_NeedsReview()
    {
        var (ctx, svc, tenantId, employeeId, device, apiKey) = await SeedAsync();
        var today = MembershipOperational.TodayCairo();
        ctx.EmployeeAttendances.Add(new EmployeeAttendance
        {
            TenantId = tenantId,
            EmployeeId = employeeId,
            AttendanceDate = today,
            CheckInAtUtc = DateTime.UtcNow.AddHours(-1),
            Status = AttendanceStatuses.Present,
            Source = AttendanceSources.Qr,
            CreatedAtUtc = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var earlier = DateTime.UtcNow.AddHours(-2);
        var result = await svc.IngestPushAsync(device.Id, apiKey, new BiometricPushBatchRequest
        {
            Events = { new BiometricPushEventRequest { VendorEventId = "E1", DeviceUserId = "1001", DeviceTimestamp = earlier.ToString("o") } }
        });
        Assert.Equal(1, result.Data!.NeedsReview);
        var att = await ctx.EmployeeAttendances.SingleAsync();
        Assert.Equal(AttendanceSources.Qr, att.Source);
    }

    [Fact]
    public async Task CrossTenant_DeviceFromOtherTenant_NotVisibleViaList()
    {
        var (ctx, svc, tenantId, _, device, _) = await SeedAsync();
        var otherTenant = Guid.NewGuid();
        var list = await svc.ListAsync(otherTenant);
        Assert.True(list.IsSuccess);
        Assert.Empty(list.Data!);
        var get = await svc.GetAsync(otherTenant, device.Id);
        Assert.False(get.IsSuccess);
    }

    [Fact]
    public async Task DuplicateEvents_DoNotInflateOvertime()
    {
        var (ctx, svc, _, employeeId, device, apiKey) = await SeedAsync(skewMinutes: 120);
        var t1 = DateTime.UtcNow.AddMinutes(-90);
        var t2 = DateTime.UtcNow.AddMinutes(-2);
        await svc.IngestPushAsync(device.Id, apiKey, new BiometricPushBatchRequest
        {
            Events =
            {
                new BiometricPushEventRequest { VendorEventId = "OT1", DeviceUserId = "1001", DeviceTimestamp = t1.ToString("o") },
                new BiometricPushEventRequest { VendorEventId = "OT2", DeviceUserId = "1001", DeviceTimestamp = t2.ToString("o") }
            }
        });
        var att = await ctx.EmployeeAttendances.SingleAsync(a => a.EmployeeId == employeeId);
        var otBefore = att.OvertimeMinutes;

        await svc.IngestPushAsync(device.Id, apiKey, new BiometricPushBatchRequest
        {
            Events =
            {
                new BiometricPushEventRequest { VendorEventId = "OT1", DeviceUserId = "1001", DeviceTimestamp = t1.ToString("o") },
                new BiometricPushEventRequest { VendorEventId = "OT2", DeviceUserId = "1001", DeviceTimestamp = t2.ToString("o") }
            }
        });
        att = await ctx.EmployeeAttendances.SingleAsync(a => a.EmployeeId == employeeId);
        Assert.Equal(otBefore, att.OvertimeMinutes);
    }

    [Fact]
    public async Task ApiKeyHash_NeverReturnedInListDto()
    {
        var (_, svc, tenantId, _, _, apiKey) = await SeedAsync();
        var list = await svc.ListAsync(tenantId);
        var json = System.Text.Json.JsonSerializer.Serialize(list.Data);
        Assert.DoesNotContain(apiKey, json);
        Assert.DoesNotContain("ApiKeyHash", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apiKeyHash", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TemplateLikePayload_IsStillStoredOnlyAsSafeJson_NoTemplateColumn()
    {
        // Foundation rejects template-looking payloads at the controller; service stores SafePayloadJson only.
        var props = typeof(BiometricAttendanceEvent).GetProperties().Select(p => p.Name).ToHashSet();
        Assert.DoesNotContain("FingerprintTemplate", props);
        Assert.DoesNotContain("TemplateData", props);
        Assert.Contains("SafePayloadJson", props);
    }
}
