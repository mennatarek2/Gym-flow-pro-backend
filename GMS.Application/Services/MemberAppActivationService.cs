namespace GMS.Application.Services;

using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GMS.Application.Common;
using GMS.Application.DTOs.Members;
using GMS.Application.Interfaces;
using GMS.Application.Options;
using GMS.Core.Entities;
using GMS.Core.Interfaces;
using GMS.Infrastructure.Persistence;

/// <summary>
/// Generates and consumes hashed one-time Member App activation codes.
/// Does not issue JWTs — AuthService owns token issuance after consume.
/// </summary>
public class MemberAppActivationService : IMemberAppActivationService
{
    // Crockford-ish alphabet without ambiguous 0/O/1/I
    private const string Alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const int CodeLength = 8;

    private readonly GymFlowProDbContext _db;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditService _audit;
    private readonly MemberAppActivationOptions _options;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MemberAppActivationService> _logger;

    public MemberAppActivationService(
        GymFlowProDbContext db,
        ITenantContext tenantContext,
        IAuditService audit,
        IOptions<MemberAppActivationOptions> options,
        IConfiguration configuration,
        ILogger<MemberAppActivationService> logger)
    {
        _db = db;
        _tenantContext = tenantContext;
        _audit = audit;
        _options = options.Value;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<Result<MemberAppActivationCodeResponse>> GenerateAsync(
        Guid memberId,
        Guid? createdByIdentityUserId,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId;
        var member = await _db.GymMembers
            .FirstOrDefaultAsync(m => m.Id == memberId && m.TenantId == tenantId && !m.IsDeleted, cancellationToken);

        if (member == null)
            return Result<MemberAppActivationCodeResponse>.Failure("Member not found / العضو غير موجود");

        if (!member.IsActive)
            return Result<MemberAppActivationCodeResponse>.Failure(
                "Unable to activate this account. / لا يمكن تفعيل هذا الحساب.");

        var hours = Math.Clamp(_options.ExpirationHours <= 0 ? 24 : _options.ExpirationHours, 1, 24 * 30);
        var now = DateTime.UtcNow;
        var expires = now.AddHours(hours);

        var actives = await _db.MemberAppActivationCodes
            .Where(c => c.TenantId == tenantId
                        && c.MemberId == memberId
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
        while (await _db.MemberAppActivationCodes.IgnoreQueryFilters()
                   .AnyAsync(c => c.TenantId == tenantId && c.CodeHash == hash, cancellationToken));

        var row = new MemberAppActivationCode
        {
            TenantId = tenantId,
            MemberId = memberId,
            CodeHash = hash,
            ExpiresAtUtc = expires,
            CreatedByUserId = createdByIdentityUserId,
            CreatedAtUtc = now
        };
        _db.MemberAppActivationCodes.Add(row);
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync(
            "member_app.activation_code.generate",
            "GymMember",
            memberId,
            after: new { expiresAtUtc = expires, codeId = row.Id },
            tenantIdOverride: tenantId);

        _logger.LogInformation(
            "Generated Member App activation code {CodeId} for member {MemberId} (expires {ExpiresAtUtc:o})",
            row.Id, memberId, expires);

        return Result<MemberAppActivationCodeResponse>.Success(new MemberAppActivationCodeResponse
        {
            MemberId = memberId,
            ActivationCode = FormatDisplay(plaintext),
            ExpiresAtUtc = expires,
            ExpiresInMinutes = (int)Math.Round((expires - now).TotalMinutes)
        });
    }

    public async Task<MemberAppAccessStatusDto> GetStatusAsync(
        Guid memberId,
        Guid tenantId,
        Guid? appUserId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var pending = await _db.MemberAppActivationCodes
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId
                        && c.MemberId == memberId
                        && c.ConsumedAtUtc == null
                        && c.RevokedAtUtc == null
                        && c.ExpiresAtUtc > now)
            .OrderByDescending(c => c.CreatedAtUtc)
            .Select(c => new { c.ExpiresAtUtc })
            .FirstOrDefaultAsync(cancellationToken);

        var lastConsumed = await _db.MemberAppActivationCodes
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId
                        && c.MemberId == memberId
                        && c.ConsumedAtUtc != null)
            .OrderByDescending(c => c.ConsumedAtUtc)
            .Select(c => c.ConsumedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var hasLinked = appUserId.HasValue;
        string status;
        if (hasLinked)
            status = "activated";
        else if (pending != null)
            status = "pending_code";
        else
            status = "not_activated";

        return new MemberAppAccessStatusDto
        {
            Status = status,
            HasLinkedAppUser = hasLinked,
            ActivatedAtUtc = lastConsumed,
            PendingCodeExpiresAtUtc = pending?.ExpiresAtUtc
        };
    }

    public async Task<Result<GymMember>> ConsumeAsync(
        Guid tenantId,
        string activationCodePlaintext,
        CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(activationCodePlaintext);
        if (normalized.Length != CodeLength)
            return Result<GymMember>.Failure("Invalid or expired activation code.");

        var hash = HashCode(normalized);
        var now = DateTime.UtcNow;

        var code = await _db.MemberAppActivationCodes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                c => c.TenantId == tenantId
                     && c.CodeHash == hash
                     && !c.IsDeleted,
                cancellationToken);

        // Generic failures — do not leak tenant/member details
        if (code == null)
            return Result<GymMember>.Failure("Invalid or expired activation code.");

        if (code.ConsumedAtUtc != null)
            return Result<GymMember>.Failure("This activation code has already been used.");

        if (code.RevokedAtUtc != null || code.ExpiresAtUtc <= now)
            return Result<GymMember>.Failure("Invalid or expired activation code.");

        var member = await _db.GymMembers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                m => m.Id == code.MemberId
                     && m.TenantId == tenantId
                     && !m.IsDeleted,
                cancellationToken);

        if (member == null || !member.IsActive)
            return Result<GymMember>.Failure("Unable to activate this account.");

        code.ConsumedAtUtc = now;
        code.UpdatedAtUtc = now;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<GymMember>.Failure("This activation code has already been used.");
        }

        await _audit.LogAsync(
            "member_app.activation_code.consume",
            "GymMember",
            member.Id,
            after: new { codeId = code.Id },
            tenantIdOverride: tenantId);

        return Result<GymMember>.Success(member);
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
            : _configuration["JwtSettings:SecretKey"] ?? "GymFlowPro-MemberAppActivation";
        var payload = Encoding.UTF8.GetBytes(normalized + "|" + pepper);
        var hash = SHA256.HashData(payload);
        return Convert.ToHexString(hash);
    }
}
