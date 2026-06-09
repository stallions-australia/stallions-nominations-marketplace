using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.Directory;

namespace Stallions.Server.Controllers;

[ApiController]
[Route("api/stallion-directory")]
[Authorize(Policy = "StaffOnly")]
public class StallionDirectoryController : ControllerBase
{
    private readonly IDirectoryService _directory;
    public StallionDirectoryController(IDirectoryService directory) => _directory = directory;

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] bool includeInactive = false,
        [FromQuery] Guid? studDirectoryId = null)
    {
        var r = await _directory.GetStallionDirectoriesAsync(includeInactive, studDirectoryId);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var r = await _directory.GetStallionDirectoryAsync(id);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateStallionDirectoryRequest request)
    {
        var r = await _directory.CreateStallionDirectoryAsync(request);
        return r.Succeeded ? StatusCode(201, r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateStallionDirectoryRequest request)
    {
        var r = await _directory.UpdateStallionDirectoryAsync(id, request);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }
}
