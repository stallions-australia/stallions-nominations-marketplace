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
        // Reserve visibility (Staff / owning stud only) is decided in the service from the DB role.
        var r = await _listings.GetByIdAsync(id);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpGet("mine/{id:guid}")]
    [Authorize(Policy = "StudFarmAdminOnly")]
    public async Task<IActionResult> GetMineById(Guid id)
    {
        var r = await _listings.GetMineByIdAsync(id);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpGet("mine")]
    [Authorize(Policy = "StudFarmAdminOnly")]
    public async Task<IActionResult> GetMine()
    {
        var r = await _listings.GetMineAsync();
        // Serialize each listing as its concrete type (as the single-listing endpoints do). A list
        // declared as ListingDto makes System.Text.Json 10 throw: the ListingType property clashes
        // with the "listingType" discriminator under camelCase. The concrete type still writes
        // "listingType" first, which the client reads as the discriminator.
        return r.Succeeded
            ? Ok(r.Value!.Cast<object>().ToList())
            : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost("auction")]
    [Authorize(Policy = "StudFarmAdminOnly")]
    public async Task<IActionResult> CreateAuction([FromBody] CreateAuctionListingRequest request)
    {
        var r = await _listings.CreateAuctionListingAsync(request);
        return r.Succeeded ? StatusCode(201, r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "StudFarmAdminOnly")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateListingRequest request)
    {
        var r = await _listings.UpdateListingAsync(id, request);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost("{id:guid}/publish")]
    [Authorize(Policy = "StudFarmAdminOnly")]
    public async Task<IActionResult> Publish(Guid id)
    {
        var r = await _listings.PublishListingAsync(id);
        return r.Succeeded ? NoContent() : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost("{id:guid}/unpublish")]
    [Authorize(Policy = "StudFarmAdminOnly")]
    public async Task<IActionResult> Unpublish(Guid id)
    {
        var r = await _listings.UnpublishListingAsync(id);
        return r.Succeeded ? NoContent() : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost("{id:guid}/close")]
    [Authorize(Policy = "StudFarmAdminOnly")]
    public async Task<IActionResult> Close(Guid id)
    {
        var r = await _listings.CloseByStudFarmAsync(id);
        return r.Succeeded ? NoContent() : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = "StaffOnly")]
    public async Task<IActionResult> Cancel(Guid id)
    {
        var r = await _listings.CancelListingAsync(id);
        return r.Succeeded ? NoContent() : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost("{id:guid}/relist")]
    [Authorize(Policy = "StudFarmAdminOnly")]
    public async Task<IActionResult> Relist(Guid id)
    {
        var r = await _listings.RelistAsync(id);
        return r.Succeeded ? StatusCode(201, r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }
}
