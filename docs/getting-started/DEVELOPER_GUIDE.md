# 🛠️ GymFlowPro Developer Guide

**Target:** .NET 8 Backend Developers  
**Last Updated:** May 2, 2026

---

## 📑 Table of Contents

1. [Project Structure](#project-structure)
2. [Architecture Overview](#architecture-overview)
3. [Development Setup](#development-setup)
4. [Coding Standards](#coding-standards)
5. [Database Layer](#database-layer)
6. [Application Layer](#application-layer)
7. [API Layer](#api-layer)
8. [Common Patterns](#common-patterns)
9. [Testing](#testing)
10. [Deployment](#deployment)

---

## Project Structure

```
D:\GMS\GMS\
├── GMS.Api/                          # ASP.NET Core API layer
│   ├── Controllers/                  # API endpoints
│   │   ├── AuthController.cs
│   │   ├── AttendanceController.cs
│   │   ├── InvitationController.cs
│   │   ├── PaymentsController.cs
│   │   ├── HealthController.cs
│   │   └── BaseApiController.cs
│   ├── Middleware/
│   │   └── TenantMiddleware.cs       # Multi-tenancy isolation
│   ├── Hubs/
│   │   └── AttendanceHub.cs          # SignalR real-time notifications
│   ├── Filters/
│   │   └── HangfireDashboardAuthFilter.cs
│   ├── Extensions/
│   │   └── PresentationServiceExtensions.cs
│   ├── Services/
│   │   └── SignalRCheckinNotifier.cs
│   ├── Program.cs                    # Dependency injection & middleware config
│   ├── appsettings.json              # Settings
│   └── Properties/launchSettings.json # Launch profiles
│
├── GMS.Application/                  # Business logic layer
│   ├── Interfaces/
│   │   ├── IAuthService.cs
│   │   ├── ICheckinService.cs
│   │   ├── IInvitationService.cs
│   │   └── IPaymentService.cs
│   ├── DTOs/                         # Data Transfer Objects
│   │   ├── Auth/
│   │   ├── Attendance/
│   │   ├── Invitation/
│   │   └── Payment/
│   ├── Services/                     # Business logic implementation
│   └── Common/
│       └── Result.cs                 # Result wrapper pattern
│
├── GMS.Infrastructure/               # Data access layer
│   ├── Persistence/
│   │   ├── GymFlowProDbContext.cs   # Entity Framework DbContext
│   │   ├── Configurations/           # Entity configurations
│   │   └── Migrations/               # Database migrations
│   ├── Repositories/
│   │   └── Repository.cs             # Generic repository pattern
│   ├── Services/
│   │   ├── TenantContext.cs          # Tenant resolution
│   │   └── [Payment Services]
│   └── InfrastructureServiceExtensions.cs
│
├── GMS.Core/                         # Domain layer (entities, interfaces)
│   ├── Entities/                     # Domain models
│   │   ├── Tenant.cs
│   │   ├── GymMember.cs
│   │   ├── Membership.cs
│   │   ├── MembershipPlan.cs
│   │   ├── GymAttendance.cs
│   │   ├── MemberInvitation.cs
│   │   ├── AppUser.cs
│   │   └── BaseEntity.cs
│   ├── Enums/
│   │   └── GymMembershipType.cs
│   ├── Interfaces/
│   │   ├── IRepository.cs
│   │   └── ITenantContext.cs
│   ├── ValueObjects/
│   │   └── Money.cs
│   └── Exceptions/
│       └── DomainException.cs
│
├── GMS.sln                           # Solution file
└── Doc/
    ├── GymFlowPro_Documentation.docx
    └── GymFlowPro_NET_Implementation_Plan.docx
```

---

## Architecture Overview

### Layered Architecture

```
┌─────────────────────────────────────┐
│       API Layer (GMS.Api)           │
│  - Controllers, Middleware, Hubs    │
├─────────────────────────────────────┤
│    Application Layer (GMS.App)      │
│  - Services, DTOs, Interfaces       │
├─────────────────────────────────────┤
│  Infrastructure Layer (GMS.Infra)   │
│  - DbContext, Repositories, EF Core │
├─────────────────────────────────────┤
│      Core Layer (GMS.Core)          │
│  - Entities, Enums, Interfaces      │
└─────────────────────────────────────┘
```

### Design Patterns Used

1. **Repository Pattern** - Generic `Repository<T>` for data access
2. **Dependency Injection** - All services registered in DI container
3. **Result Pattern** - `Result<T>` wrapper for consistent error handling
4. **Multi-tenancy** - Tenant isolation via middleware & database filtering
5. **SignalR Hubs** - Real-time check-in notifications

---

## Development Setup

### Prerequisites

```powershell
# Check .NET version
dotnet --version
# Output: 8.0.x (or higher)

# Check SQL Server
sqlcmd -S "(localdb)\mssqllocaldb"
# Should connect successfully
```

### Initial Setup

1. **Clone repository:**
   ```powershell
   cd D:\GMS\GMS
   ```

2. **Restore packages:**
   ```powershell
   dotnet restore
   ```

3. **Configure appsettings:**
   ```powershell
   # Development settings
   cat GMS.Api\appsettings.Development.json
   
   # Should contain:
   # - "DefaultConnection" (SQL Server connection string)
   # - JWT secret/issuer/audience
   # - Payment gateway keys
   ```

4. **Apply migrations:**
   ```powershell
   dotnet ef database update --project GMS.Infrastructure --startup-project GMS.Api
   ```

5. **Run API:**
   ```powershell
   dotnet run --project GMS.Api
   # Available at: https://localhost:5001
   ```

---

## Coding Standards

### File Naming

```csharp
// Controllers
AuthController.cs              ✅ Good
auth-controller.cs             ❌ Wrong
IAuthService.cs                ✅ Good (interfaces start with I)
```

### Namespace Convention

```csharp
namespace GMS.Api.Controllers;        // Controllers
namespace GMS.Application.Services;   // Application services
namespace GMS.Infrastructure.Repositories; // Repository classes
namespace GMS.Core.Entities;          // Domain entities
```

### Class Structure

```csharp
namespace GMS.Api.Controllers;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Application.DTOs.Invitation;
using GMS.Application.Interfaces;

/// <summary>
/// Invitation endpoints for member guest invitations.
/// </summary>
[Route("api/[controller]")]
[ApiController]
public class InvitationController : BaseApiController
{
    private readonly IInvitationService _invitationService;

    // ────── CONSTRUCTOR ──────────────────────────────────────────
    public InvitationController(IInvitationService invitationService)
    {
        _invitationService = invitationService;
    }

    // ────── PUBLIC METHODS ───────────────────────────────────────
    
    /// <summary>
    /// Sends a guest invitation (atomic, monthly quota enforced).
    /// </summary>
    [HttpPost("send")]
    [Authorize(Policy = "AuthenticatedMember")]
    [ProducesResponseType(typeof(SendInvitationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SendInvitation([FromBody] SendInvitationRequest request)
    {
        var memberId = GetMemberId();
        var result = await _invitationService.SendInvitationAsync(request, memberId, GetTenantId());
        
        if (!result.IsSuccess)
            return BadRequest(new { error = result.Error });
            
        return Ok(result.Data);
    }

    // ────── PRIVATE HELPERS ──────────────────────────────────────
    
    private Guid GetMemberId()
    {
        var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }
}
```

### Async/Await Pattern

```csharp
// ✅ Good - Async all the way
public async Task<IActionResult> MyMethod()
{
    var result = await _service.GetDataAsync();
    return Ok(result);
}

// ❌ Bad - Blocking call
public async Task<IActionResult> MyMethod()
{
    var result = _service.GetData().Result;  // Causes deadlock!
    return Ok(result);
}
```

### Error Handling

```csharp
// ✅ Good - Use Result pattern
public async Task<IActionResult> SendInvitation([FromBody] SendInvitationRequest request)
{
    var result = await _invitationService.SendInvitationAsync(request, memberId, tenantId);
    
    if (!result.IsSuccess)
        return BadRequest(new { error = result.Error });
        
    return Ok(result.Data);
}

// ❌ Bad - Unhandled exceptions
public async Task<IActionResult> SendInvitation([FromBody] SendInvitationRequest request)
{
    var result = await _invitationService.SendInvitationAsync(request, memberId, tenantId);
    return Ok(result);  // What if result is null?
}
```

---

## Database Layer

### Entity Framework DbContext

**Location:** `GMS.Infrastructure/Persistence/GymFlowProDbContext.cs`

```csharp
public class GymFlowProDbContext : DbContext
{
    public DbSet<Tenant> Tenants { get; set; }
    public DbSet<GymMember> GymMembers { get; set; }
    public DbSet<Membership> Memberships { get; set; }
    public DbSet<MembershipPlan> MembershipPlans { get; set; }
    public DbSet<GymAttendance> Attendances { get; set; }
    public DbSet<MemberInvitation> Invitations { get; set; }
    public DbSet<AppUser> Users { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // Apply all entity configurations
        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(GymFlowProDbContext).Assembly);
    }
}
```

### Entity Configurations

**Example:** `GMS.Infrastructure/Persistence/Configurations/GymMemberConfiguration.cs`

```csharp
public class GymMemberConfiguration : IEntityTypeConfiguration<GymMember>
{
    public void Configure(EntityTypeBuilder<GymMember> builder)
    {
        builder.HasKey(x => x.Id);

        builder.Property(x => x.FullName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.PhoneNumber)
            .IsRequired()
            .HasMaxLength(20);

        // One-to-many: Tenant -> Members
        builder.HasOne(x => x.Tenant)
            .WithMany(x => x.Members)
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        // One-to-many: Member -> Memberships
        builder.HasMany(x => x.Memberships)
            .WithOne(x => x.Member)
            .HasForeignKey(x => x.MemberId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

### Migrations

```powershell
# Create migration
dotnet ef migrations add MigrationName --project GMS.Infrastructure --startup-project GMS.Api

# Apply migration
dotnet ef database update --project GMS.Infrastructure --startup-project GMS.Api

# List all migrations
dotnet ef migrations list --project GMS.Infrastructure --startup-project GMS.Api

# Revert last migration
dotnet ef migrations remove --project GMS.Infrastructure --startup-project GMS.Api
```

---

## Application Layer

### Service Interface Pattern

**Location:** `GMS.Application/Interfaces/IInvitationService.cs`

```csharp
public interface IInvitationService
{
    Task<Result<SendInvitationResponse>> SendInvitationAsync(
        SendInvitationRequest request,
        Guid memberId,
        Guid tenantId);
    
    Task<Result<List<InvitationHistoryResponse>>> GetMemberInvitationsAsync(
        Guid memberId,
        Guid tenantId);
}
```

### Service Implementation

**Location:** `GMS.Application/Services/InvitationService.cs`

```csharp
public class InvitationService : IInvitationService
{
    private readonly IRepository<MemberInvitation> _invitationRepository;
    private readonly IRepository<GymMember> _memberRepository;
    private readonly ILogger<InvitationService> _logger;

    public InvitationService(
        IRepository<MemberInvitation> invitationRepository,
        IRepository<GymMember> memberRepository,
        ILogger<InvitationService> logger)
    {
        _invitationRepository = invitationRepository;
        _memberRepository = memberRepository;
        _logger = logger;
    }

    public async Task<Result<SendInvitationResponse>> SendInvitationAsync(
        SendInvitationRequest request,
        Guid memberId,
        Guid tenantId)
    {
        try
        {
            // Validate member exists
            var member = await _memberRepository.GetByIdAsync(memberId);
            if (member == null)
                return Result<SendInvitationResponse>.Failure("Member not found");

            // Check monthly quota (atomic)
            var thisMonth = DateTime.UtcNow.Date.AddDays(1 - DateTime.UtcNow.Day);
            var invitationsThisMonth = await _invitationRepository.QueryAsync(
                x => x.MemberId == memberId 
                  && x.TenantId == tenantId
                  && x.CreatedAt >= thisMonth);

            if (invitationsThisMonth.Count >= 3)
                return Result<SendInvitationResponse>.Failure(
                    "Monthly invitation quota exceeded (3/3)");

            // Create invitation
            var invitation = MemberInvitation.Create(
                memberId,
                tenantId,
                request.GuestName,
                request.GuestPhone);

            await _invitationRepository.AddAsync(invitation);
            await _invitationRepository.SaveChangesAsync();

            return Result<SendInvitationResponse>.Success(
                new SendInvitationResponse
                {
                    InvitationId = invitation.Id,
                    GuestName = invitation.GuestName,
                    GuestPhone = invitation.GuestPhone,
                    SentAt = invitation.CreatedAt,
                    ExpiresAt = invitation.ExpirationDate,
                    RemainingQuota = 3 - (invitationsThisMonth.Count + 1)
                });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending invitation for member {MemberId}", memberId);
            return Result<SendInvitationResponse>.Failure(
                "An error occurred while sending the invitation");
        }
    }
}
```

### Result Pattern

**Location:** `GMS.Application/Common/Result.cs`

```csharp
public class Result<T>
{
    public bool IsSuccess { get; private set; }
    public T? Data { get; private set; }
    public string? Error { get; private set; }

    public static Result<T> Success(T data) =>
        new Result<T> { IsSuccess = true, Data = data };

    public static Result<T> Failure(string error) =>
        new Result<T> { IsSuccess = false, Error = error };
}
```

---

## API Layer

### Controller Structure

```csharp
[Route("api/[controller]")]
[ApiController]
[Authorize]  // Require auth for all endpoints unless marked [AllowAnonymous]
public class InvitationController : BaseApiController
{
    [HttpPost("send")]
    [ProducesResponseType(typeof(SendInvitationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SendInvitation([FromBody] SendInvitationRequest request)
    {
        // Implementation
    }
}
```

### Base Controller Helpers

**Location:** `GMS.Api/Controllers/BaseApiController.cs`

```csharp
public class BaseApiController : ControllerBase
{
    /// <summary>
    /// Extracts JWT "sub" claim (ApplicationUser ID)
    /// </summary>
    protected Guid GetUserId()
    {
        var sub = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(sub, out var id) ? id : Guid.Empty;
    }

    /// <summary>
    /// Extracts JWT "tenant_id" claim for multi-tenancy
    /// </summary>
    protected Guid GetTenantId()
    {
        var tenantClaim = User.FindFirst("tenant_id")?.Value;
        return Guid.TryParse(tenantClaim, out var id) ? id : Guid.Empty;
    }

    /// <summary>
    /// Gets client IP address
    /// </summary>
    protected string GetIpAddress()
    {
        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
    }
}
```

---

## Common Patterns

### Multi-Tenancy Pattern

```csharp
// TenantMiddleware filters all queries by current tenant
public class TenantMiddleware
{
    private readonly RequestDelegate _next;

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext)
    {
        var tenantId = context.User.FindFirst("tenant_id")?.Value;
        if (Guid.TryParse(tenantId, out var id))
        {
            tenantContext.SetTenant(id);
        }

        await _next(context);
    }
}

// Services use TenantContext to get current tenant
public class SomeService
{
    private readonly ITenantContext _tenantContext;

    public async Task<Result<T>> GetDataAsync()
    {
        var tenantId = _tenantContext.GetTenant();
        var data = await _repo.QueryAsync(x => x.TenantId == tenantId);
        return Result<T>.Success(data);
    }
}
```

### Authentication Token Claims

```csharp
// JWT tokens contain these claims
public class JwtTokenClaims
{
    public Guid UserId { get; set; }          // "sub" claim
    public Guid TenantId { get; set; }        // "tenant_id" claim
    public string Email { get; set; }         // "email" claim
    public string[] Roles { get; set; }       // "role" claim
}

// Extract in controllers
var userId = User.FindFirst(ClaimTypes.NameIdentifier).Value;
var tenantId = User.FindFirst("tenant_id").Value;
```

### Rate Limiting (Check-in)

```csharp
// In Program.cs
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("checkin-policy", context =>
        RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: context.User.FindFirst("sub")?.Value,
            factory: partition => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1)
            }));
});

// In controller
[EnableRateLimiting("checkin-policy")]
public async Task<IActionResult> QrCheckin([FromBody] QrCheckinRequest request)
{
    // Only 10 check-ins per minute per member
}
```

---

## Testing

### Unit Test Structure

```csharp
[TestClass]
public class InvitationServiceTests
{
    private Mock<IRepository<MemberInvitation>> _mockRepo;
    private InvitationService _service;

    [TestInitialize]
    public void Setup()
    {
        _mockRepo = new Mock<IRepository<MemberInvitation>>();
        _service = new InvitationService(_mockRepo.Object, /* ... */);
    }

    [TestMethod]
    public async Task SendInvitation_WithValidRequest_ReturnsSuccess()
    {
        // Arrange
        var request = new SendInvitationRequest { GuestName = "Ahmed", GuestPhone = "+201234567890" };
        var memberId = Guid.NewGuid();

        // Act
        var result = await _service.SendInvitationAsync(request, memberId, Guid.NewGuid());

        // Assert
        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("Ahmed", result.Data.GuestName);
    }

    [TestMethod]
    public async Task SendInvitation_ExceedsQuota_ReturnsFail()
    {
        // Arrange - Setup 3 existing invitations this month

        // Act
        var result = await _service.SendInvitationAsync(request, memberId, tenantId);

        // Assert
        Assert.IsFalse(result.IsSuccess);
        Assert.IsTrue(result.Error.Contains("quota"));
    }
}
```

### Running Tests

```powershell
# Run all tests
dotnet test

# Run specific test class
dotnet test --filter "ClassName=InvitationServiceTests"

# Run with verbosity
dotnet test --verbosity detailed
```

---

## Deployment

### Build for Production

```powershell
# Build release version
dotnet build -c Release

# Publish to folder
dotnet publish -c Release -o ./publish

# Check output
ls ./publish
```

### Environment Configuration

```json
// appsettings.Production.json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=YOUR_SQL_HOST;Database=GymFlowProDb;User Id=YOUR_USER;Password=YOUR_PASSWORD;Encrypt=True;TrustServerCertificate=True;"
  },
  "JwtSettings": {
    "SecretKey": "YOUR_PRODUCTION_JWT_SECRET_MIN_32_CHARS",
    "Issuer": "https://gymflowpro.com",
    "Audience": "https://gymflowpro.com",
    "ExpirationMinutes": 15
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning"
    }
  }
}
```

---

## Quick Reference

### Common Commands

```powershell
# Development
dotnet run --project GMS.Api

# Build
dotnet build

# Clean
dotnet clean

# Restore packages
dotnet restore

# Database
dotnet ef database update --project GMS.Infrastructure --startup-project GMS.Api

# Tests
dotnet test

# Swagger UI
https://localhost:5001/swagger/ui
```

---

**Happy Coding! 🚀**

