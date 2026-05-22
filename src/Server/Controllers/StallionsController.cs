using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.Stallions;

namespace Stallions.Server.Controllers;

[ApiController]
[Route("api/stallions")]
public class StallionsController : ControllerBase
{
    private readonly IStallionService _stallions;
    public StallionsController(IStallionService stallions) => _stallions = stallions;

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetAll()
    {
        var r = await _stallions.GetAllWithActiveListingsAsync();
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpGet("mine")]
    [Authorize(Roles = "StudFarmAdmin")]
    public async Task<IActionResult> GetMine()
    {
        var r = await _stallions.GetByStudFarmAsync();
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetById(Guid id)
    {
        var r = await _stallions.GetByIdAsync(id, User.IsInRole("Staff") || User.IsInRole("StudFarmAdmin"));
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost]
    [Authorize(Roles = "StudFarmAdmin")]
    public async Task<IActionResult> Create([FromBody] CreateStallionRequest request)
    {
        var r = await _stallions.CreateAsync(request);
        return r.Succeeded ? StatusCode(201, r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "StudFarmAdmin")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateStallionRequest request)
    {
        var r = await _stallions.UpdateAsync(id, request);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost("{id:guid}/images")]
    [Authorize(Roles = "StudFarmAdmin")]
    [RequestSizeLimit(10_485_760)]
    [RequestFormLimits(MultipartBodyLengthLimit = 10_485_760)]
    public async Task<IActionResult> UploadImage(Guid id, [FromForm] IFormFile file)
    {
        var r = await _stallions.UploadImageAsync(id, file);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPut("{id:guid}/images/{imageId:guid}/primary")]
    [Authorize(Roles = "StudFarmAdmin")]
    public async Task<IActionResult> SetPrimaryImage(Guid id, Guid imageId)
    {
        var r = await _stallions.SetPrimaryImageAsync(id, imageId);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpDelete("{id:guid}/images/{imageId:guid}")]
    [Authorize(Roles = "StudFarmAdmin")]
    public async Task<IActionResult> DeleteImage(Guid id, Guid imageId)
    {
        var r = await _stallions.DeleteImageAsync(id, imageId);
        return r.Succeeded ? NoContent() : StatusCode(r.HttpStatusCode, r.Error);
    }
}
