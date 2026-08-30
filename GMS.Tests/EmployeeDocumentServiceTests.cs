namespace GMS.Tests;

using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using GMS.Application.DTOs.Hr;
using GMS.Application.Interfaces;
using GMS.Application.Services;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;
using GMS.Infrastructure.Services;

public class EmployeeDocumentServiceTests
{
    private sealed class NoOpAudit : IAuditService
    {
        public Task LogAsync(string action, string? entityType = null, Guid? entityId = null, object? before = null, object? after = null, Guid? tenantIdOverride = null)
            => Task.CompletedTask;

        public Task<GMS.Application.Common.Result<GMS.Application.Common.PagedResult<GMS.Application.DTOs.Audit.AuditEventDto>>> GetAuditEventsAsync(
            Guid tenantId, GMS.Application.DTOs.Audit.AuditEventQueryRequest query)
            => Task.FromResult(GMS.Application.Common.Result<GMS.Application.Common.PagedResult<GMS.Application.DTOs.Audit.AuditEventDto>>.Failure("n/a"));
    }

    /// <summary>In-memory fake that actually round-trips bytes, so DownloadAsync can be tested for real.</summary>
    private sealed class FakeFileStorage : IFileStorageService
    {
        private readonly Dictionary<string, byte[]> _blobs = new();

        public async Task<string> UploadAsync(Stream stream, string fileName, string folder)
        {
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            var url = $"/uploads/{folder}/{Guid.NewGuid():N}-{fileName}";
            _blobs[url] = ms.ToArray();
            return url;
        }

        public Task DeleteAsync(string fileUrl)
        {
            _blobs.Remove(fileUrl);
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string fileUrl) => Task.FromResult(_blobs.ContainsKey(fileUrl));
        public Task<byte[]?> TryReadAsync(string fileUrl) => Task.FromResult(_blobs.TryGetValue(fileUrl, out var b) ? b : null);
    }

    private static async Task<(GymFlowProDbContext ctx, EmployeeDocumentService svc, Guid tenantId, Guid employeeId)> SeedAsync()
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
            FirstName = "Ahmed",
            LastName = "Mohamed",
            HireDate = new DateOnly(2026, 1, 1),
            CreatedAtUtc = DateTime.UtcNow
        };
        ctx.Employees.Add(employee);
        await ctx.SaveChangesAsync();

        var svc = new EmployeeDocumentService(ctx, new FakeFileStorage(), new NoOpAudit(), NullLogger<EmployeeDocumentService>.Instance);
        return (ctx, svc, tenantId, employee.Id);
    }

    private static MemoryStream Bytes(string content) => new(Encoding.UTF8.GetBytes(content));

    [Fact]
    public async Task UploadAsync_ThenDownloadAsync_RoundTripsBytes()
    {
        var (_, svc, tenantId, employeeId) = await SeedAsync();

        var uploaded = await svc.UploadAsync(tenantId, employeeId, Bytes("hello id card"), "id.pdf", "application/pdf",
            new CreateEmployeeDocumentRequest { DocumentType = EmployeeDocumentTypes.NationalId }, null);
        Assert.True(uploaded.IsSuccess, uploaded.Error);

        var downloaded = await svc.DownloadAsync(tenantId, uploaded.Data!.Id);
        Assert.True(downloaded.IsSuccess, downloaded.Error);
        Assert.Equal("hello id card", Encoding.UTF8.GetString(downloaded.Data.Bytes));
        Assert.Equal("id.pdf", downloaded.Data.FileName);
    }

    [Fact]
    public async Task DownloadAsync_RestrictedToWrongEmployee_Fails()
    {
        var (ctx, svc, tenantId, employeeId) = await SeedAsync();
        var uploaded = await svc.UploadAsync(tenantId, employeeId, Bytes("secret"), "id.pdf", "application/pdf",
            new CreateEmployeeDocumentRequest { DocumentType = EmployeeDocumentTypes.NationalId }, null);

        var otherEmployeeId = Guid.NewGuid();
        var result = await svc.DownloadAsync(tenantId, uploaded.Data!.Id, restrictToEmployeeId: otherEmployeeId);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task ExpiryStatus_ComputesExpiredExpiringSoonAndValid()
    {
        var (_, svc, tenantId, employeeId) = await SeedAsync();
        var today = GMS.Core.Utilities.MembershipOperational.TodayCairo();

        var expired = await svc.UploadAsync(tenantId, employeeId, Bytes("a"), "a.pdf", "application/pdf",
            new CreateEmployeeDocumentRequest { DocumentType = EmployeeDocumentTypes.Contract, ExpiryDate = today.AddDays(-5) }, null);
        var soon = await svc.UploadAsync(tenantId, employeeId, Bytes("b"), "b.pdf", "application/pdf",
            new CreateEmployeeDocumentRequest { DocumentType = EmployeeDocumentTypes.Contract, ExpiryDate = today.AddDays(10) }, null);
        var valid = await svc.UploadAsync(tenantId, employeeId, Bytes("c"), "c.pdf", "application/pdf",
            new CreateEmployeeDocumentRequest { DocumentType = EmployeeDocumentTypes.Contract, ExpiryDate = today.AddDays(90) }, null);
        var noExpiry = await svc.UploadAsync(tenantId, employeeId, Bytes("d"), "d.pdf", "application/pdf",
            new CreateEmployeeDocumentRequest { DocumentType = EmployeeDocumentTypes.Other }, null);

        Assert.Equal(DocumentExpiryStatuses.Expired, expired.Data!.ExpiryStatus);
        Assert.Equal(DocumentExpiryStatuses.ExpiringSoon, soon.Data!.ExpiryStatus);
        Assert.Equal(DocumentExpiryStatuses.Valid, valid.Data!.ExpiryStatus);
        Assert.Equal(DocumentExpiryStatuses.Valid, noExpiry.Data!.ExpiryStatus);
    }

    [Fact]
    public async Task UploadAsync_RejectsExpiryBeforeIssue()
    {
        var (_, svc, tenantId, employeeId) = await SeedAsync();

        var result = await svc.UploadAsync(tenantId, employeeId, Bytes("x"), "x.pdf", "application/pdf",
            new CreateEmployeeDocumentRequest { DocumentType = EmployeeDocumentTypes.Contract, IssueDate = new DateOnly(2026, 6, 1), ExpiryDate = new DateOnly(2026, 5, 1) }, null);

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task DeleteAsync_RemovesFromListing()
    {
        var (_, svc, tenantId, employeeId) = await SeedAsync();
        var uploaded = await svc.UploadAsync(tenantId, employeeId, Bytes("x"), "x.pdf", "application/pdf",
            new CreateEmployeeDocumentRequest { DocumentType = EmployeeDocumentTypes.Other }, null);

        var deleted = await svc.DeleteAsync(tenantId, uploaded.Data!.Id, null);
        Assert.True(deleted.IsSuccess, deleted.Error);

        var list = await svc.ListAsync(tenantId, employeeId);
        Assert.Empty(list.Data!);
    }

    [Fact]
    public async Task Documents_AreTenantIsolated()
    {
        var (_, svcA, tenantA, employeeA) = await SeedAsync();
        var (_, svcB, tenantB, employeeB) = await SeedAsync();

        await svcA.UploadAsync(tenantA, employeeA, Bytes("a"), "a.pdf", "application/pdf", new CreateEmployeeDocumentRequest { DocumentType = EmployeeDocumentTypes.Other }, null);
        await svcB.UploadAsync(tenantB, employeeB, Bytes("b"), "b.pdf", "application/pdf", new CreateEmployeeDocumentRequest { DocumentType = EmployeeDocumentTypes.Other }, null);

        var listA = await svcA.ListAsync(tenantA, employeeA);
        Assert.Single(listA.Data!);
    }
}
