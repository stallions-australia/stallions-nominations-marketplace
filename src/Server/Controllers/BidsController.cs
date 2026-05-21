using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.Bids;

namespace Stallions.Server.Controllers;

[ApiController]
public class BidsController : ControllerBase
{
    private readonly IBidService _bids;

    public BidsController(IBidService bids) => _bids = bids;

    // Public — amount only, no buyer identity
    [HttpGet("api/listings/{id:guid}/bids/current")]
    [AllowAnonymous]
    public async Task<IActionResult> GetCurrent(Guid id)
    {
        var r = await _bids.GetCurrentBidAsync(id);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost("api/listings/{id:guid}/bids")]
    [Authorize(Roles = "Buyer")]
    public async Task<IActionResult> PlaceBid(Guid id, [FromBody] PlaceBidRequest request)
    {
        var r = await _bids.PlaceBidAsync(id, request);
        return r.Succeeded ? StatusCode(201, r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    // Full history with buyer IDs — Staff only
    [HttpGet("api/listings/{id:guid}/bids")]
    [Authorize(Roles = "Staff")]
    public async Task<IActionResult> GetHistory(Guid id)
    {
        var r = await _bids.GetHistoryAsync(id);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpGet("api/bids/mine")]
    [Authorize(Roles = "Buyer")]
    public async Task<IActionResult> GetMine()
    {
        var r = await _bids.GetMineAsync();
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }
}
