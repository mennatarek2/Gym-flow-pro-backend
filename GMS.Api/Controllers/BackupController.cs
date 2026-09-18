namespace GMS.Api.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Api.Filters;
using GMS.Application.Interfaces;

/// <summary>
/// Backup &amp; Recovery visibility for the Local Edition Settings page. OwnerOnly + Local Edition
/// only (404 on SaaS, which has its own separate MonsterASP backup story, not this one).
/// Read-only except TriggerManualBackupAsync, which only launches the existing backup script as a
/// detached process - see BackupHealthService's class remarks for why restore is intentionally
/// NOT exposed here.
/// </summary>
[Route("api/backup")]
[Authorize(Policy = "OwnerOnly")]
[RequireLocalEdition]
public class BackupController : BaseApiController
{
    private readonly IBackupHealthService _backupHealth;

    public BackupController(IBackupHealthService backupHealth)
    {
        _backupHealth = backupHealth;
    }

    /// <summary>GET /api/backup/health</summary>
    [HttpGet("health")]
    public async Task<IActionResult> GetHealth()
    {
        return Ok(await _backupHealth.GetHealthAsync());
    }

    /// <summary>GET /api/backup/history</summary>
    [HttpGet("history")]
    public async Task<IActionResult> GetHistory([FromQuery] int take = 30)
    {
        return Ok(await _backupHealth.GetHistoryAsync(take));
    }

    /// <summary>POST /api/backup/run</summary>
    [HttpPost("run")]
    public async Task<IActionResult> TriggerBackup()
    {
        var result = await _backupHealth.TriggerManualBackupAsync();
        if (!result.Started)
            return BadRequest(result);
        return Ok(result);
    }
}
