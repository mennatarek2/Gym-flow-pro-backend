namespace GMS.Api.Controllers;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GMS.Api.Authorization;
using GMS.Application.DTOs.Expenses;
using GMS.Application.Interfaces;
using GMS.Core.Constants;
using GMS.Core.Interfaces;

[Route("api/expenses")]
[Authorize(Roles = "Owner,Manager")]
public sealed class CashExpensesController : BaseApiController
{
    private readonly ICashExpenseService _expenses;
    private readonly ITenantContext _tenantContext;

    public CashExpensesController(ICashExpenseService expenses, ITenantContext tenantContext)
    {
        _expenses = expenses;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [HasPermission(Permissions.ReportsExpensesView)]
    public async Task<IActionResult> List(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken ct)
    {
        var result = await _expenses.ListAsync(_tenantContext.TenantId, from, to, ct);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(new { error = result.Error });
    }

    [HttpPost]
    [HasPermission(Permissions.ReportsExpensesManage)]
    public async Task<IActionResult> Create(
        [FromBody] CreateCashExpenseRequest request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized(new { error = "Authenticated staff user required." });

        var result = await _expenses.CreateAsync(_tenantContext.TenantId, userId, request, ct);
        return result.IsSuccess
            ? Created("/api/expenses", result.Data)
            : BadRequest(new { error = result.Error });
    }

    [HttpPatch("{id:guid}")]
    [HasPermission(Permissions.ReportsExpensesManage)]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateCashExpenseRequest request,
        CancellationToken ct)
    {
        var result = await _expenses.UpdateAsync(_tenantContext.TenantId, id, request, ct);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(new { error = result.Error });
    }

    private bool TryGetUserId(out Guid userId)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? User.FindFirstValue("sub");
        return Guid.TryParse(value, out userId);
    }
}
