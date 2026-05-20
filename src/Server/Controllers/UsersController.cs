using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.Users;
using Stallions.Shared.Enums;

namespace Stallions.Server.Controllers;

[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly IUserService _users;
    public UsersController(IUserService users) => _users = users;

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> GetMe()
    {
        var r = await _users.GetCurrentUserAsync();
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPut("me")]
    [Authorize]
    public async Task<IActionResult> UpdateMe([FromBody] UpdateProfileRequest request)
    {
        var r = await _users.UpdateCurrentUserAsync(request);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpGet]
    [Authorize(Roles = "Staff")]
    public async Task<IActionResult> GetAll([FromQuery] UserRole? role, [FromQuery] UserStatus? status)
    {
        var r = await _users.GetAllAsync(role, status);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost("{id:guid}/verify")]
    [Authorize(Roles = "Staff")]
    public async Task<IActionResult> Verify(Guid id)
    {
        var r = await _users.VerifyUserAsync(id);
        return r.Succeeded ? NoContent() : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost("{id:guid}/suspend")]
    [Authorize(Roles = "Staff")]
    public async Task<IActionResult> Suspend(Guid id)
    {
        var r = await _users.SuspendUserAsync(id);
        return r.Succeeded ? NoContent() : StatusCode(r.HttpStatusCode, r.Error);
    }
}
