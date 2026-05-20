using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.Seasons;

namespace Stallions.Server.Controllers;

[ApiController]
[Route("api/seasons")]
public class SeasonsController : ControllerBase
{
    private readonly ISeasonService _seasons;
    public SeasonsController(ISeasonService seasons) => _seasons = seasons;

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> GetAll()
    {
        var r = await _seasons.GetAllAsync();
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpGet("current")]
    [AllowAnonymous]
    public async Task<IActionResult> GetCurrent()
    {
        var r = await _seasons.GetCurrentAsync();
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost]
    [Authorize(Roles = "Staff")]
    public async Task<IActionResult> Create([FromBody] CreateSeasonRequest request)
    {
        var r = await _seasons.CreateAsync(request);
        return r.Succeeded ? StatusCode(201, r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Staff")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateSeasonRequest request)
    {
        var r = await _seasons.UpdateAsync(id, request);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost("{id:guid}/open")]
    [Authorize(Roles = "Staff")]
    public async Task<IActionResult> Open(Guid id)
    {
        var r = await _seasons.OpenSeasonAsync(id);
        return r.Succeeded ? NoContent() : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost("{id:guid}/close")]
    [Authorize(Roles = "Staff")]
    public async Task<IActionResult> Close(Guid id)
    {
        var r = await _seasons.CloseSeasonAsync(id);
        return r.Succeeded ? NoContent() : StatusCode(r.HttpStatusCode, r.Error);
    }
}
