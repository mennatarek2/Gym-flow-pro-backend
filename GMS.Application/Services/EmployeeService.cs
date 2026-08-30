namespace GMS.Application.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using GMS.Application.Common;
using GMS.Application.DTOs.Hr;
using GMS.Application.DTOs.Notifications;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;

public class EmployeeService : IEmployeeService
{
    private readonly GymFlowProDbContext _db;
    private readonly IAuditService _audit;
    private readonly IFileStorageService _files;
    private readonly IAdminService? _admin;
    private readonly IStaffNotificationPublisher? _staffNotifications;
    private readonly ILogger<EmployeeService> _logger;

    public EmployeeService(
        GymFlowProDbContext db,
        IAuditService audit,
        IFileStorageService files,
        ILogger<EmployeeService> logger,
        IAdminService? admin = null,
        IStaffNotificationPublisher? staffNotifications = null)
    {
        _db = db;
        _audit = audit;
        _files = files;
        _logger = logger;
        _admin = admin;
        _staffNotifications = staffNotifications;
    }

    public async Task<Result<List<EmployeeListItemDto>>> ListAsync(
        Guid tenantId, string? status = null, Guid? departmentId = null, string? search = null)
    {
        var q = _db.Employees.AsNoTracking().Where(e => e.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(e => e.Status == status);
        if (departmentId.HasValue)
            q = q.Where(e => e.DepartmentId == departmentId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            q = q.Where(e =>
                e.FirstName.Contains(term) || e.LastName.Contains(term) ||
                e.EmployeeNumber.Contains(term) ||
                (e.Phone != null && e.Phone.Contains(term)) ||
                (e.Email != null && e.Email.Contains(term)));
        }

        var rows = await q.OrderBy(e => e.FirstName).ThenBy(e => e.LastName).ToListAsync();
        var departmentNames = await _db.Departments.AsNoTracking().Where(d => d.TenantId == tenantId).ToDictionaryAsync(d => d.Id, d => d.Name);
        var positionNames = await _db.Positions.AsNoTracking().Where(p => p.TenantId == tenantId).ToDictionaryAsync(p => p.Id, p => p.Name);

        return Result<List<EmployeeListItemDto>>.Success(
            rows.Select(e => MapListItem(e, departmentNames, positionNames)).ToList());
    }

    public async Task<Result<EmployeeDto>> GetAsync(Guid tenantId, Guid id)
    {
        var entity = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id && e.TenantId == tenantId);
        if (entity == null)
            return Result<EmployeeDto>.Failure("Employee not found / الموظف غير موجود");

        return Result<EmployeeDto>.Success(await MapDetailAsync(tenantId, entity));
    }

    public async Task<Result<EmployeeDto>> CreateAsync(Guid tenantId, CreateEmployeeRequest request)
    {
        var firstName = request.FirstName?.Trim() ?? string.Empty;
        var lastName = request.LastName?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(firstName) || string.IsNullOrEmpty(lastName))
            return Result<EmployeeDto>.Failure("First and last name are required / الاسم الأول والأخير مطلوبان");

        var validation = await ValidateReferencesAsync(tenantId, request.DepartmentId, request.PositionId, request.AppUserId, existingEmployeeId: null);
        if (validation != null)
            return Result<EmployeeDto>.Failure(validation);

        var entity = new Employee
        {
            TenantId = tenantId,
            EmployeeNumber = await AllocateEmployeeNumberAsync(tenantId),
            FirstName = firstName,
            LastName = lastName,
            Phone = Normalize(request.Phone),
            Email = Normalize(request.Email),
            NationalId = Normalize(request.NationalId),
            DateOfBirth = request.DateOfBirth,
            Address = Normalize(request.Address),
            HireDate = request.HireDate,
            Status = EmployeeStatuses.Active,
            DepartmentId = request.DepartmentId,
            PositionId = request.PositionId,
            AppUserId = request.AppUserId,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.Employees.Add(entity);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("employee.create", "Employee", entity.Id, null, new EmployeeAuditSnapshot(entity));
        _logger.LogInformation("Employee {Number} created for tenant {TenantId}", entity.EmployeeNumber, tenantId);

        if (_staffNotifications != null)
        {
            await _staffNotifications.TryPublishAsync(tenantId, new CreateStaffNotificationRequest
            {
                Type = StaffNotificationTypes.EmployeeActivated,
                Category = StaffNotificationCategories.Staff,
                Priority = StaffNotificationPriorities.Info,
                Title = "Employee created",
                TitleAr = "تم إنشاء موظف",
                Body = $"{entity.FirstName} {entity.LastName} ({entity.EmployeeNumber}) was added.",
                BodyAr = $"تمت إضافة {entity.FirstName} {entity.LastName} ({entity.EmployeeNumber}).",
                EntityType = "Employee",
                EntityId = entity.Id,
                ActionUrl = $"/dashboard/hr/employees/?id={entity.Id}",
                DedupeKey = $"employee-activated:{entity.Id:N}",
                RecipientRoles = new[] { "Owner", "Manager" }
            });
        }

        return Result<EmployeeDto>.Success(await MapDetailAsync(tenantId, entity));
    }

    public async Task<Result<EmployeeDto>> UpdateAsync(Guid tenantId, Guid id, UpdateEmployeeRequest request)
    {
        var entity = await _db.Employees.FirstOrDefaultAsync(e => e.Id == id && e.TenantId == tenantId);
        if (entity == null)
            return Result<EmployeeDto>.Failure("Employee not found / الموظف غير موجود");

        var firstName = request.FirstName?.Trim() ?? string.Empty;
        var lastName = request.LastName?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(firstName) || string.IsNullOrEmpty(lastName))
            return Result<EmployeeDto>.Failure("First and last name are required / الاسم الأول والأخير مطلوبان");

        var status = request.Status?.Trim() ?? string.Empty;
        if (status != EmployeeStatuses.Active && status != EmployeeStatuses.Suspended)
            return Result<EmployeeDto>.Failure(
                "Status must be Active or Suspended here — use terminate for termination / الحالة يجب أن تكون نشط أو موقوف، استخدم إنهاء الخدمة للإنهاء");

        var validation = await ValidateReferencesAsync(tenantId, request.DepartmentId, request.PositionId, request.AppUserId, existingEmployeeId: id);
        if (validation != null)
            return Result<EmployeeDto>.Failure(validation);

        var before = new EmployeeAuditSnapshot(entity);

        entity.FirstName = firstName;
        entity.LastName = lastName;
        entity.Phone = Normalize(request.Phone);
        entity.Email = Normalize(request.Email);
        entity.NationalId = Normalize(request.NationalId);
        entity.DateOfBirth = request.DateOfBirth;
        entity.Address = Normalize(request.Address);
        entity.DepartmentId = request.DepartmentId;
        entity.PositionId = request.PositionId;
        entity.AppUserId = request.AppUserId;
        entity.Status = status;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await _audit.LogAsync("employee.update", "Employee", entity.Id, before, new EmployeeAuditSnapshot(entity));

        return Result<EmployeeDto>.Success(await MapDetailAsync(tenantId, entity));
    }

    public async Task<Result<EmployeeDto>> TerminateAsync(Guid tenantId, Guid id, TerminateEmployeeRequest request)
    {
        var entity = await _db.Employees.FirstOrDefaultAsync(e => e.Id == id && e.TenantId == tenantId);
        if (entity == null)
            return Result<EmployeeDto>.Failure("Employee not found / الموظف غير موجود");

        if (entity.Status == EmployeeStatuses.Terminated)
            return Result<EmployeeDto>.Failure("Employee is already terminated / الموظف منتهي الخدمة بالفعل");

        if (request.TerminationDate < entity.HireDate)
            return Result<EmployeeDto>.Failure("Termination date cannot be before hire date / تاريخ إنهاء الخدمة لا يمكن أن يسبق تاريخ التعيين");

        var before = new EmployeeAuditSnapshot(entity);

        entity.Status = EmployeeStatuses.Terminated;
        entity.TerminationDate = request.TerminationDate;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await _audit.LogAsync("employee.terminate", "Employee", entity.Id, before, new EmployeeAuditSnapshot(entity));
        _logger.LogInformation("Employee {Number} terminated for tenant {TenantId}", entity.EmployeeNumber, tenantId);

        return Result<EmployeeDto>.Success(await MapDetailAsync(tenantId, entity));
    }

    public async Task<Result<EmployeeDto>> SetPhotoAsync(Guid tenantId, Guid id, Stream image, string fileName, string contentType)
    {
        var entity = await _db.Employees.FirstOrDefaultAsync(e => e.Id == id && e.TenantId == tenantId);
        if (entity == null)
            return Result<EmployeeDto>.Failure("Employee not found / الموظف غير موجود");

        var folder = $"employee-photos-{tenantId:N}";
        var relativeUrl = await _files.UploadAsync(image, fileName, folder);
        entity.PhotoUrl = relativeUrl;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await _audit.LogAsync("employee.photo.update", "Employee", entity.Id, null, new { photo = true });

        return Result<EmployeeDto>.Success(await MapDetailAsync(tenantId, entity));
    }

    public async Task<Result<List<EmployeeContractDto>>> ListContractsAsync(Guid tenantId, Guid employeeId)
    {
        var employeeExists = await _db.Employees.AnyAsync(e => e.Id == employeeId && e.TenantId == tenantId);
        if (!employeeExists)
            return Result<List<EmployeeContractDto>>.Failure("Employee not found / الموظف غير موجود");

        var contracts = await _db.EmployeeContracts.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.EmployeeId == employeeId)
            .OrderByDescending(c => c.StartDate)
            .ToListAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return Result<List<EmployeeContractDto>>.Success(contracts.Select(c => Map(c, today)).ToList());
    }

    public async Task<Result<EmployeeContractDto>> AddContractAsync(Guid tenantId, Guid employeeId, CreateEmployeeContractRequest request)
    {
        var employee = await _db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId && e.TenantId == tenantId);
        if (employee == null)
            return Result<EmployeeContractDto>.Failure("Employee not found / الموظف غير موجود");

        var employmentType = request.EmploymentType?.Trim() ?? string.Empty;
        if (!EmploymentTypes.All.Contains(employmentType))
            return Result<EmployeeContractDto>.Failure("Invalid employment type / نوع التوظيف غير صالح");

        if (request.EndDate.HasValue && request.EndDate.Value <= request.StartDate)
            return Result<EmployeeContractDto>.Failure("End date must be after start date / تاريخ الانتهاء يجب أن يكون بعد تاريخ البدء");

        if (request.BasicSalary < 0)
            return Result<EmployeeContractDto>.Failure("Basic salary cannot be negative / الراتب الأساسي لا يمكن أن يكون سالباً");

        var status = string.IsNullOrWhiteSpace(request.Status) ? ContractStatuses.Active : request.Status.Trim();
        if (!ContractStatuses.All.Contains(status))
            return Result<EmployeeContractDto>.Failure("Invalid contract status / حالة العقد غير صالحة");

        var existing = await _db.EmployeeContracts.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.EmployeeId == employeeId)
            .ToListAsync();

        var overlaps = existing.Any(c => Overlaps(request.StartDate, request.EndDate, c.StartDate, c.EndDate));
        if (overlaps)
            return Result<EmployeeContractDto>.Failure(
                "This contract's dates overlap an existing contract for this employee / تواريخ هذا العقد تتداخل مع عقد آخر لنفس الموظف");

        var entity = new EmployeeContract
        {
            TenantId = tenantId,
            EmployeeId = employeeId,
            ContractNumber = await AllocateContractNumberAsync(tenantId),
            EmploymentType = employmentType,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            BasicSalary = request.BasicSalary,
            WorkingHoursPerDay = request.WorkingHoursPerDay,
            WorkingDaysPerWeek = request.WorkingDaysPerWeek,
            Status = status,
            Notes = Normalize(request.Notes),
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.EmployeeContracts.Add(entity);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("employee.contract.create", "EmployeeContract", entity.Id, null,
            new { entity.ContractNumber, entity.EmployeeId, entity.EmploymentType, entity.StartDate, entity.EndDate, entity.BasicSalary });

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return Result<EmployeeContractDto>.Success(Map(entity, today));
    }

    public Task<Result<EmployeeContractDto?>> GetCurrentContractAsync(Guid tenantId, Guid employeeId)
        => GetContractAsOfAsync(tenantId, employeeId, DateOnly.FromDateTime(DateTime.UtcNow));

    public async Task<Result<EmployeeContractDto?>> GetContractAsOfAsync(Guid tenantId, Guid employeeId, DateOnly asOfDate)
    {
        var employeeExists = await _db.Employees.AnyAsync(e => e.Id == employeeId && e.TenantId == tenantId);
        if (!employeeExists)
            return Result<EmployeeContractDto?>.Failure("Employee not found / الموظف غير موجود");

        var current = await _db.EmployeeContracts.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.EmployeeId == employeeId
                && c.Status == ContractStatuses.Active && c.StartDate <= asOfDate
                && (c.EndDate == null || c.EndDate >= asOfDate))
            .OrderByDescending(c => c.StartDate)
            .FirstOrDefaultAsync();

        return Result<EmployeeContractDto?>.Success(current == null ? null : Map(current, asOfDate));
    }

    public async Task<Guid?> ResolveEmployeeIdForCallerAsync(Guid tenantId, Guid identityUserId)
    {
        var appUserId = await ResolveAppUserIdForCallerAsync(tenantId, identityUserId);
        if (appUserId == null)
            return null;

        // Employee App identity (EmployeeAppUserId) OR optional Staff link (AppUserId).
        // Self-service requires Active status — Suspended/Terminated JWTs must not bypass.
        var employee = await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e =>
                e.TenantId == tenantId
                && (e.EmployeeAppUserId == appUserId || e.AppUserId == appUserId));

        if (employee == null)
            return null;

        if (!string.Equals(employee.Status, EmployeeStatuses.Active, StringComparison.OrdinalIgnoreCase))
            return null;

        return employee.Id;
    }

    public async Task<Guid?> ResolveAppUserIdForCallerAsync(Guid tenantId, Guid identityUserId)
    {
        var identityIdStr = identityUserId.ToString();
        var appUser = await _db.AppUsers.AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == identityIdStr && u.TenantId == tenantId);
        return appUser?.Id;
    }

    /// <summary>Half-open [start,end) overlap check. A null end is treated as open-ended (MaxValue).</summary>
    private static bool Overlaps(DateOnly aStart, DateOnly? aEnd, DateOnly bStart, DateOnly? bEnd) =>
        aStart < (bEnd ?? DateOnly.MaxValue) && bStart < (aEnd ?? DateOnly.MaxValue);

    private async Task<string?> ValidateReferencesAsync(
        Guid tenantId, Guid? departmentId, Guid? positionId, Guid? appUserId, Guid? existingEmployeeId)
    {
        if (departmentId.HasValue)
        {
            var departmentExists = await _db.Departments.AnyAsync(d => d.Id == departmentId && d.TenantId == tenantId);
            if (!departmentExists)
                return "Department not found / القسم غير موجود";
        }

        if (positionId.HasValue)
        {
            var positionExists = await _db.Positions.AnyAsync(p => p.Id == positionId && p.TenantId == tenantId);
            if (!positionExists)
                return "Position not found / المسمى الوظيفي غير موجود";
        }

        if (appUserId.HasValue)
        {
            var appUser = await _db.AppUsers.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == appUserId && a.TenantId == tenantId);
            if (appUser == null)
                return "Linked staff account not found / حساب الموظف المرتبط غير موجود";

            // Employee App identities (Role=Employee) must not be used as Staff desk links.
            if (string.Equals(appUser.Role, "Employee", StringComparison.OrdinalIgnoreCase)
                || string.Equals(appUser.Role, "Member", StringComparison.OrdinalIgnoreCase))
                return "Cannot link an Employee App or Member account as Staff / لا يمكن ربط حساب تطبيق الموظف كحساب موظف إداري";

            var alreadyLinked = await _db.Employees.AnyAsync(e =>
                e.TenantId == tenantId && e.AppUserId == appUserId && e.Id != existingEmployeeId);
            if (alreadyLinked)
                return "This staff account is already linked to another employee / هذا الحساب مرتبط بموظف آخر بالفعل";
        }

        return null;
    }

    private async Task<string> AllocateEmployeeNumberAsync(Guid tenantId)
    {
        var existing = await _db.Employees.IgnoreQueryFilters()
            .Where(e => e.TenantId == tenantId)
            .Select(e => e.EmployeeNumber)
            .ToListAsync();

        return $"EMP-{(NextSequence(existing, "EMP-")):D4}";
    }

    private async Task<string> AllocateContractNumberAsync(Guid tenantId)
    {
        var existing = await _db.EmployeeContracts.IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId)
            .Select(c => c.ContractNumber)
            .ToListAsync();

        return $"CT-{(NextSequence(existing, "CT-")):D4}";
    }

    private static int NextSequence(IEnumerable<string> values, string prefix)
    {
        var max = 0;
        foreach (var raw in values)
        {
            if (raw.Length > prefix.Length &&
                raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(raw.AsSpan(prefix.Length), out var n) && n > max)
            {
                max = n;
            }
        }
        return max + 1;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<EmployeeDto> MapDetailAsync(Guid tenantId, Employee e)
    {
        var departmentName = e.DepartmentId.HasValue
            ? await _db.Departments.AsNoTracking().Where(d => d.Id == e.DepartmentId).Select(d => d.Name).FirstOrDefaultAsync()
            : null;
        var positionName = e.PositionId.HasValue
            ? await _db.Positions.AsNoTracking().Where(p => p.Id == e.PositionId).Select(p => p.Name).FirstOrDefaultAsync()
            : null;

        var dto = new EmployeeDto
        {
            Id = e.Id,
            EmployeeNumber = e.EmployeeNumber,
            FirstName = e.FirstName,
            LastName = e.LastName,
            Phone = e.Phone,
            Email = e.Email,
            PhotoUrl = e.PhotoUrl,
            Status = e.Status,
            DepartmentId = e.DepartmentId,
            DepartmentName = departmentName,
            PositionId = e.PositionId,
            PositionName = positionName,
            HasLogin = e.AppUserId.HasValue,
            HireDate = e.HireDate,
            NationalId = e.NationalId,
            DateOfBirth = e.DateOfBirth,
            Address = e.Address,
            TerminationDate = e.TerminationDate,
            AppUserId = e.AppUserId,
            CreatedAtUtc = e.CreatedAtUtc,
            UpdatedAtUtc = e.UpdatedAtUtc
        };

        if (e.AppUserId.HasValue)
        {
            dto.StaffAccount = await ResolveStaffAccountAsync(tenantId, e.AppUserId.Value);
        }

        return dto;
    }

    /// <summary>Builds the System Access summary for a linked AppUserId. FullName/Email/Role come from
    /// IAdminService (the single source of truth for Staff identity/role) rather than AppUser's own
    /// FirstName/LastName/Role fields, which aren't kept in sync with Identity role assignment.</summary>
    private async Task<StaffAccountDto> ResolveStaffAccountAsync(Guid tenantId, Guid appUserId)
    {
        var missing = new StaffAccountDto { AppUserId = appUserId, Status = "Missing" };
        if (_admin == null)
            return missing;

        var linkedAppUser = await _db.AppUsers.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == appUserId && a.TenantId == tenantId);
        if (linkedAppUser == null || !Guid.TryParse(linkedAppUser.UserId, out var identityId))
            return missing;

        var staffResult = await _admin.GetStaffUserByIdAsync(tenantId, identityId);
        if (!staffResult.IsSuccess || staffResult.Data == null)
            return missing;

        return new StaffAccountDto
        {
            AppUserId = appUserId,
            FullName = staffResult.Data.FullName,
            Email = staffResult.Data.Email,
            Role = staffResult.Data.Role,
            Status = staffResult.Data.IsActive ? "Active" : "Disabled"
        };
    }

    public async Task<Result<List<AvailableStaffDto>>> ListAvailableStaffAsync(Guid tenantId, Guid employeeId)
    {
        var employeeExists = await _db.Employees.AnyAsync(e => e.Id == employeeId && e.TenantId == tenantId);
        if (!employeeExists)
            return Result<List<AvailableStaffDto>>.Failure("Employee not found / الموظف غير موجود");

        if (_admin == null)
            return Result<List<AvailableStaffDto>>.Success(new List<AvailableStaffDto>());

        var staffResult = await _admin.GetStaffUsersAsync(tenantId);
        if (!staffResult.IsSuccess)
            return Result<List<AvailableStaffDto>>.Success(new List<AvailableStaffDto>());

        var linkedElsewhere = await _db.Employees.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.AppUserId != null && e.Id != employeeId)
            .Select(e => e.AppUserId!.Value)
            .ToListAsync();
        var linkedSet = new HashSet<Guid>(linkedElsewhere);

        var available = staffResult.Data!
            .Where(s => s.AppUserId.HasValue && !linkedSet.Contains(s.AppUserId.Value))
            .Select(s => new AvailableStaffDto
            {
                AppUserId = s.AppUserId!.Value,
                FullName = s.FullName,
                Email = s.Email,
                Role = s.Role,
                IsActive = s.IsActive,
                StaffNumber = s.StaffNumber
            })
            .ToList();

        return Result<List<AvailableStaffDto>>.Success(available);
    }

    public async Task<Result<EmployeeDto>> LinkStaffAsync(Guid tenantId, Guid employeeId, Guid appUserId)
    {
        var entity = await _db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId && e.TenantId == tenantId);
        if (entity == null)
            return Result<EmployeeDto>.Failure("Employee not found / الموظف غير موجود");

        var validation = await ValidateReferencesAsync(tenantId, null, null, appUserId, existingEmployeeId: employeeId);
        if (validation != null)
            return Result<EmployeeDto>.Failure(validation);

        var before = new EmployeeAuditSnapshot(entity);
        entity.AppUserId = appUserId;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await _audit.LogAsync("employee.link_staff", "Employee", entity.Id, before, new EmployeeAuditSnapshot(entity));

        return Result<EmployeeDto>.Success(await MapDetailAsync(tenantId, entity));
    }

    public async Task<Result<EmployeeDto>> UnlinkStaffAsync(Guid tenantId, Guid employeeId)
    {
        var entity = await _db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId && e.TenantId == tenantId);
        if (entity == null)
            return Result<EmployeeDto>.Failure("Employee not found / الموظف غير موجود");
        if (entity.AppUserId == null)
            return Result<EmployeeDto>.Failure("Employee has no linked staff account / لا يوجد حساب دخول مرتبط بهذا الموظف");

        var before = new EmployeeAuditSnapshot(entity);
        entity.AppUserId = null;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await _audit.LogAsync("employee.unlink_staff", "Employee", entity.Id, before, new EmployeeAuditSnapshot(entity));

        return Result<EmployeeDto>.Success(await MapDetailAsync(tenantId, entity));
    }

    public async Task<Result<EmployeeMeDto>> GetMeAsync(Guid tenantId, Guid identityUserId)
    {
        var employeeId = await ResolveEmployeeIdForCallerAsync(tenantId, identityUserId);
        if (employeeId == null)
            return Result<EmployeeMeDto>.Failure("Employee not found / الموظف غير موجود");

        var entity = await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId.Value && e.TenantId == tenantId);
        if (entity == null)
            return Result<EmployeeMeDto>.Failure("Employee not found / الموظف غير موجود");

        string? departmentName = null;
        string? positionName = null;
        if (entity.DepartmentId.HasValue)
            departmentName = await _db.Departments.AsNoTracking()
                .Where(d => d.Id == entity.DepartmentId && d.TenantId == tenantId)
                .Select(d => d.Name)
                .FirstOrDefaultAsync();
        if (entity.PositionId.HasValue)
            positionName = await _db.Positions.AsNoTracking()
                .Where(p => p.Id == entity.PositionId && p.TenantId == tenantId)
                .Select(p => p.Name)
                .FirstOrDefaultAsync();

        return Result<EmployeeMeDto>.Success(new EmployeeMeDto
        {
            Id = entity.Id,
            EmployeeNumber = entity.EmployeeNumber,
            FirstName = entity.FirstName,
            LastName = entity.LastName,
            FullName = $"{entity.FirstName} {entity.LastName}".Trim(),
            Phone = entity.Phone,
            Email = entity.Email,
            PhotoUrl = entity.PhotoUrl,
            Status = entity.Status,
            DepartmentId = entity.DepartmentId,
            DepartmentName = departmentName,
            PositionId = entity.PositionId,
            PositionName = positionName,
            HireDate = entity.HireDate,
            DateOfBirth = entity.DateOfBirth,
            CreatedAtUtc = entity.CreatedAtUtc
        });
    }

    private static EmployeeListItemDto MapListItem(
        Employee e, IReadOnlyDictionary<Guid, string> departmentNames, IReadOnlyDictionary<Guid, string> positionNames) => new()
    {
        Id = e.Id,
        EmployeeNumber = e.EmployeeNumber,
        FirstName = e.FirstName,
        LastName = e.LastName,
        Phone = e.Phone,
        Email = e.Email,
        PhotoUrl = e.PhotoUrl,
        Status = e.Status,
        DepartmentId = e.DepartmentId,
        DepartmentName = e.DepartmentId.HasValue && departmentNames.TryGetValue(e.DepartmentId.Value, out var dn) ? dn : null,
        PositionId = e.PositionId,
        PositionName = e.PositionId.HasValue && positionNames.TryGetValue(e.PositionId.Value, out var pn) ? pn : null,
        HasLogin = e.AppUserId.HasValue,
        HireDate = e.HireDate
    };

    private static EmployeeContractDto Map(EmployeeContract c, DateOnly today) => new()
    {
        Id = c.Id,
        EmployeeId = c.EmployeeId,
        ContractNumber = c.ContractNumber,
        EmploymentType = c.EmploymentType,
        StartDate = c.StartDate,
        EndDate = c.EndDate,
        BasicSalary = c.BasicSalary,
        WorkingHoursPerDay = c.WorkingHoursPerDay,
        WorkingDaysPerWeek = c.WorkingDaysPerWeek,
        Status = c.Status,
        Notes = c.Notes,
        IsCurrent = c.Status == ContractStatuses.Active && c.StartDate <= today && (c.EndDate == null || c.EndDate >= today),
        CreatedAtUtc = c.CreatedAtUtc
    };
}
