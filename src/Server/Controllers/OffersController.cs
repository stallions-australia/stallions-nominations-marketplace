using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.Offers;

namespace Stallions.Server.Controllers;

/// <summary>Private offers on Make an Offer listings.</summary>
[ApiController]
[Route("api")]
public class OffersController : ControllerBase
{
    private readonly IOfferService _offers;
    public OffersController(IOfferService offers) => _offers = offers;

    [HttpPost("listings/{id:guid}/offers")]
    [Authorize(Policy = "BuyerOnly")]
    public async Task<IActionResult> Make(Guid id, [FromBody] MakeOfferRequest request)
    {
        var r = await _offers.MakeAsync(id, request);
        return r.Succeeded ? StatusCode(201, r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpGet("listings/{id:guid}/offers")]
    [Authorize(Policy = "StudFarmOrStaff")]
    public async Task<IActionResult> ForListing(Guid id)
    {
        var r = await _offers.GetForListingAsync(id);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpGet("listings/{id:guid}/my-offer")]
    [Authorize(Policy = "BuyerOnly")]
    public async Task<IActionResult> MyOffer(Guid id)
    {
        var r = await _offers.GetMyOfferForListingAsync(id);
        if (!r.Succeeded) return StatusCode(r.HttpStatusCode, r.Error);
        return r.Value == null ? NoContent() : Ok(r.Value);
    }

    [HttpGet("offers/mine")]
    [Authorize(Policy = "BuyerOnly")]
    public async Task<IActionResult> Mine()
    {
        var r = await _offers.GetMineAsync();
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost("offers/{id:guid}/withdraw")]
    [Authorize(Policy = "BuyerOnly")]
    public async Task<IActionResult> Withdraw(Guid id)
    {
        var r = await _offers.WithdrawAsync(id);
        return r.Succeeded ? NoContent() : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost("offers/{id:guid}/accept")]
    [Authorize(Policy = "StudFarmAdminOnly")]
    public async Task<IActionResult> Accept(Guid id)
    {
        var r = await _offers.AcceptAsync(id);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost("offers/{id:guid}/decline")]
    [Authorize(Policy = "StudFarmAdminOnly")]
    public async Task<IActionResult> Decline(Guid id)
    {
        var r = await _offers.DeclineAsync(id);
        return r.Succeeded ? NoContent() : StatusCode(r.HttpStatusCode, r.Error);
    }
}
