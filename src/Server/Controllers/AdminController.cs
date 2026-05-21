using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.Admin;

namespace Stallions.Server.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Staff")]
public class AdminController : ControllerBase
{
    private readonly IAdminService _admin;
    public AdminController(IAdminService admin) => _admin = admin;

    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard()
    {
        var r = await _admin.GetDashboardAsync();
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpGet("transactions")]
    public async Task<IActionResult> GetTransactions()
    {
        var r = await _admin.GetTransactionsAsync();
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpGet("invoices")]
    public async Task<IActionResult> GetInvoices()
    {
        var r = await _admin.GetInvoicesAsync();
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPut("listings/{id:guid}/fee")]
    public async Task<IActionResult> SetListingFee(Guid id, [FromBody] SetListingFeeRequest request)
    {
        var r = await _admin.SetListingFeeAsync(id, request);
        return r.Succeeded ? NoContent() : StatusCode(r.HttpStatusCode, r.Error);
    }
}
