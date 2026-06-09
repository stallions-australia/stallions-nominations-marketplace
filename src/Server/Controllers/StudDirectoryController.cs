using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.Directory;

namespace Stallions.Server.Controllers;

[ApiController]
[Route("api/stud-directory")]
[Authorize(Policy = "StaffOnly")]
public class StudDirectoryController : ControllerBase
{
    private readonly IDirectoryService _directory;
    public StudDirectoryController(IDirectoryService directory) => _directory = directory;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] bool includeInactive = false)
    {
        var r = await _directory.GetStudDirectoriesAsync(includeInactive);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var r = await _directory.GetStudDirectoryAsync(id);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateStudDirectoryRequest request)
    {
        var r = await _directory.CreateStudDirectoryAsync(request);
        return r.Succeeded ? StatusCode(201, r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateStudDirectoryRequest request)
    {
        var r = await _directory.UpdateStudDirectoryAsync(id, request);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }
}
