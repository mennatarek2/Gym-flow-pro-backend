namespace GMS.Application.Services;

using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GMS.Application.Common;
using GMS.Application.DTOs.Hr;
using GMS.Application.Interfaces;
using GMS.Application.Options;
using GMS.Core.Constants;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Generates and consumes hashed one-time Employee App activation codes.
/// Does not issue JWTs — AuthService owns token issuance after consume.
/// </summary>
public class EmployeeAppActivationService : IEmployeeAppActivationService
{
    private const string Alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const int CodeLength = 8;

    private readonly GymFlowProDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _audit;
    private readonly EmployeeAppActivationOptions _options;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmployeeAppActivationService> _logger;

    public EmployeeAppActivationService(
        GymFlowProDbContext db,
        ITenantContext tenantContext,
        IAuditService audit,
        IOptions<EmployeeAppActivationOptions> options,
        IConfiguration configuration,
        ILogger<EmployeeAppActivationService> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _audit = audit;
        _options = options.Value;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<Result<EmployeeAppActivationCodeResponse>> GenerateAsync(
        Guid employeeId,
        Guid? createdByIdentityUserId,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId;
        var employee = await _db.Employees
            .FirstOrDefaultAsync(e => e.Id == employeeId && e.TenantId == tenantId && !e.IsDeleted, cancellationToken);

        if (employee == null)
            return Result<EmployeeAppActivationCodeResponse>.Failure("Employee not found / الموظف غير موجود");

        if (!string.Equals(employee.Status, EmployeeStatuses.Active, StringComparison.OrdinalIgnoreCase))
            return Result<EmployeeAppActivationCodeResponse>.Failure(
                "Unable to activate this account. / لا يمكن تفعيل هذا الحساب.");

        var hours = Math.Clamp(_options.ExpirationHours <= 0 ? 24 : _options.ExpirationHours, 1, 24 * 30);
        var now = DateTime.UtcNow;
        var expires = now.AddHours(hours);

        var actives = await _db.EmployeeAppActivationCodes
            .Where(c => c.TenantId == tenantId
                        && c.EmployeeId == employeeId
                        && c.ConsumedAtUtc == null
                        && c.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);

        foreach (var prev in actives)
            prev.RevokedAtUtc = now;

        string plaintext;
        string hash;
        do
        {
            plaintext = CreatePlaintextCode();
            hash = HashCode(plaintext);
        }
        while (await _db.EmployeeAppActivationCodes.IgnoreQueryFilters()
                   .AnyAsync(c => c.TenantId == tenantId && c.CodeHash == hash, cancellationToken));

        var row = new EmployeeAppActivationCode
        {
            TenantId = tenantId,
            EmployeeId = employeeId,
            CodeHash = hash,
            ExpiresAtUtc = expires,
            CreatedByUserId = createdByIdentityUserId,
            CreatedAtUtc = now
        };
        _db.EmployeeAppActivationCodes.Add(row);
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(
            "employee.app_activation_code.generated",
            "Employee",
            employeeId,
            after: new { expiresAtUtc = expires, codeId = row.Id },
            tenantIdOverride: tenantId);

        _logger.LogInformation(
            "Generated Employee App activation code {CodeId} for employee {EmployeeId} (expires {ExpiresAtUtc:o})",
            row.Id, employeeId, expires);

        return Result<EmployeeAppActivationCodeResponse>.Success(new EmployeeAppActivationCodeResponse
        {
            EmployeeId = employeeId,
            EmployeeNumber = employee.EmployeeNumber,
            ActivationCode = FormatDisplay(plaintext),
            ExpiresAtUtc = expires,
            ExpiresInMinutes = (int)Math.Round((expires - now).TotalMinutes)
        });
    }

    public async Task<Result<Employee>> ConsumeAsync(
        Guid tenantId,
        string activationCodePlaintext,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(activationCodePlaintext);
        if (normalized.Length != CodeLength)
            return Result<Employee>.Failure("Invalid or expired activation code.");

        var hash = HashCode(normalized);
        var now = DateTime.UtcNow;

        var code = await _db.EmployeeAppActivationCodes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                c => c.TenantId == tenantId
                     && c.CodeHash == hash
                     && !c.IsDeleted,
                cancellationToken);

        if (code == null)
            return Result<Employee>.Failure("Invalid or expired activation code.");

        if (code.ConsumedAtUtc != null)
            return Result<Employee>.Failure("This activation code has already been used.");

        if (code.RevokedAtUtc != null || code.ExpiresAtUtc <= now)
            return Result<Employee>.Failure("Invalid or expired activation code.");

        var employee = await _db.Employees
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                e => e.Id == code.EmployeeId
                     && e.TenantId == tenantId
                     && !e.IsDeleted,
                cancellationToken);

        if (employee == null
            || !string.Equals(employee.Status, EmployeeStatuses.Active, StringComparison.OrdinalIgnoreCase))
            return Result<Employee>.Failure("Unable to activate this account.");

        code.ConsumedAtUtc = now;
        code.UpdatedAtUtc = now;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<Employee>.Failure("This activation code has already been used.");
        }

        await _audit.LogAsync(
            "employee.app_activation.completed",
            "Employee",
            employee.Id,
            after: new { codeId = code.Id },
            tenantIdOverride: tenantId);

        return Result<Employee>.Success(employee);
    }

    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var sb = new StringBuilder(CodeLength);
        foreach (var ch in raw.Trim().ToUpperInvariant())
        {
            if (ch == '-' || ch == ' ') continue;
            sb.Append(ch);
        }
        return sb.ToString();
    }

    private static string FormatDisplay(string normalized) =>
        normalized.Length == CodeLength
            ? $"{normalized[..4]}-{normalized[4..]}"
            : normalized;

    private static string CreatePlaintextCode()
    {
        var bytes = RandomNumberGenerator.GetBytes(CodeLength);
        var chars = new char[CodeLength];
        for (var i = 0; i < CodeLength; i++)
            chars[i] = Alphabet[bytes[i] % Alphabet.Length];
        return new string(chars);
    }

    private string HashCode(string input)
    {
        var normalized = Normalize(input);
        var pepper = !string.IsNullOrWhiteSpace(_options.CodePepper)
            ? _options.CodePepper
            : _configuration["JwtSettings:SecretKey"] ?? "GymFlowPro-EmployeeAppActivation";
        var payload = Encoding.UTF8.GetBytes(normalized + "|" + pepper);
        var hash = SHA256.HashData(payload);
        return Convert.ToHexString(hash);
    }
}
