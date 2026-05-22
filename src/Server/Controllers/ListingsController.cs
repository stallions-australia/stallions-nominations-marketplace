using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.Listings;
using Stallions.Shared.Enums;

namespace Stallions.Server.Controllers;

[ApiController]
[Route("api/listings")]
public class ListingsController : ControllerBase
{
    private readonly IListingService _listings;
    public ListingsController(IListingService listings) => _listings = listings;

    private bool IsStaff => User.IsInRole("Staff");

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetActive(
        [FromQuery] Guid? seasonId,
        [FromQuery] Guid? studFarmId,
        [FromQuery] string? type)
    {
        var r = await _listings.GetListingCardsAsync(seasonId, studFarmId, type);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetById(Guid id)
    {
        var r = await _listings.GetByIdAsync(id, IsStaff);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpGet("mine")]
    [Authorize(Roles = "StudFarmAdmin")]
    public async Task<IActionResult> GetMine()
    {
        var r = await _listings.GetMineAsync();
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost("auction")]
    [Authorize(Roles = "StudFarmAdmin")]
    public async Task<IActionResult> CreateAuction([FromBody] CreateAuctionListingRequest request)
    {
        var r = await _listings.CreateAuctionListingAsync(request);
        return r.Succeeded ? StatusCode(201, r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost("fixed-price")]
    [Authorize(Roles = "StudFarmAdmin")]
    public async Task<IActionResult> CreateFixedPrice([FromBody] CreateFixedPriceListingRequest request)
    {
        var r = await _listings.CreateFixedPriceListingAsync(request);
        return r.Succeeded ? StatusCode(201, r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "StudFarmAdmin")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateListingRequest request)
    {
        var r = await _listings.UpdateListingAsync(id, request);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost("{id:guid}/publish")]
    [Authorize(Roles = "StudFarmAdmin")]
    public async Task<IActionResult> Publish(Guid id)
    {
        var r = await _listings.PublishListingAsync(id);
        return r.Succeeded ? NoContent() : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost("{id:guid}/unpublish")]
    [Authorize(Roles = "StudFarmAdmin")]
    public async Task<IActionResult> Unpublish(Guid id)
    {
        var r = await _listings.UnpublishListingAsync(id);
        return r.Succeeded ? NoContent() : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost("{id:guid}/close")]
    [Authorize(Roles = "StudFarmAdmin")]
    public async Task<IActionResult> Close(Guid id)
    {
        var r = await _listings.CloseByStudFarmAsync(id);
        return r.Succeeded ? NoContent() : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = "Staff")]
    public async Task<IActionResult> Cancel(Guid id)
    {
        var r = await _listings.CancelListingAsync(id);
        return r.Succeeded ? NoContent() : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost("{id:guid}/relist")]
    [Authorize(Roles = "StudFarmAdmin")]
    public async Task<IActionResult> Relist(Guid id)
    {
        var r = await _listings.RelistAsync(id);
        return r.Succeeded ? StatusCode(201, r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }
}
