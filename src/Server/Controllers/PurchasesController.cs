using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stallions.Server.Services;

namespace Stallions.Server.Controllers;

/// <summary>Sale records. They are created only by the auction closer — there is no checkout endpoint.</summary>
[ApiController]
[Route("api")]
public class PurchasesController : ControllerBase
{
    private readonly IPurchaseService _purchases;

    public PurchasesController(IPurchaseService purchases) => _purchases = purchases;

    [HttpGet("purchases")]
    [Authorize]
    public async Task<IActionResult> GetAll()
    {
        var r = await _purchases.GetPurchasesAsync();
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpGet("purchases/{id:guid}")]
    [Authorize]
    public async Task<IActionResult> GetById(Guid id)
    {
        var r = await _purchases.GetPurchaseByIdAsync(id);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpGet("listings/{id:guid}/my-result")]
    [Authorize(Policy = "BuyerOnly")]
    public async Task<IActionResult> GetMyAuctionResult(Guid id)
    {
        var r = await _purchases.GetMyAuctionResultAsync(id);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost("purchases/{id:guid}/refund")]
    [Authorize(Policy = "StaffOnly")]
    public async Task<IActionResult> Refund(Guid id)
    {
        var r = await _purchases.RefundAsync(id);
        return r.Succeeded ? NoContent() : StatusCode(r.HttpStatusCode, r.Error);
    }
}
