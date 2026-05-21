using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stallions.Server.Services;

namespace Stallions.Server.Controllers;

[ApiController]
[Route("api/bindings")]
[Authorize]
public class BindingsController : ControllerBase
{
    private readonly INominationBindingService _bindings;

    public BindingsController(INominationBindingService bindings) => _bindings = bindings;

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var r = await _bindings.GetByIdAsync(id);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost("{id:guid}/acknowledge")]
    [Authorize(Roles = "StudFarmAdmin")]
    public async Task<IActionResult> Acknowledge(Guid id)
    {
        var r = await _bindings.AcknowledgeAsync(id);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost("{id:guid}/sign")]
    public async Task<IActionResult> Sign(Guid id)
    {
        var r = await _bindings.SignAsync(id);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost("{id:guid}/dispute")]
    [Authorize(Roles = "Staff")]
    public async Task<IActionResult> Dispute(Guid id)
    {
        var r = await _bindings.DisputeAsync(id);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }
}
