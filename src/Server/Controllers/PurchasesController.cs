using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.Checkout;

namespace Stallions.Server.Controllers;

[ApiController]
[Route("api")]
public class PurchasesController : ControllerBase
{
    private readonly ICheckoutService _checkout;

    public PurchasesController(ICheckoutService checkout) => _checkout = checkout;

    [HttpPost("listings/{id:guid}/checkout")]
    [Authorize(Roles = "Buyer")]
    public async Task<IActionResult> Initiate(Guid id, [FromBody] CheckoutRequest request)
    {
        var r = await _checkout.InitiateCheckoutAsync(id, request);
        return r.Succeeded ? StatusCode(201, r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost("purchases/{id:guid}/complete")]
    [AllowAnonymous]
    public async Task<IActionResult> Complete(Guid id)
    {
        var secret = Request.Headers["X-Webhook-Secret"].FirstOrDefault();
        var r = await _checkout.CompleteCheckoutAsync(id, secret);
        return r.Succeeded ? NoContent() : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpGet("purchases")]
    [Authorize]
    public async Task<IActionResult> GetAll()
    {
        var r = await _checkout.GetPurchasesAsync();
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpGet("purchases/{id:guid}")]
    [Authorize]
    public async Task<IActionResult> GetById(Guid id)
    {
        var r = await _checkout.GetPurchaseByIdAsync(id);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost("purchases/{id:guid}/refund")]
    [Authorize(Roles = "Staff")]
    public async Task<IActionResult> Refund(Guid id)
    {
        var r = await _checkout.RefundAsync(id);
        return r.Succeeded ? NoContent() : StatusCode(r.HttpStatusCode, r.Error);
    }
}
