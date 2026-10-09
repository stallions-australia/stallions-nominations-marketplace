using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.Settings;

namespace Stallions.Server.Controllers;

[ApiController]
[Route("api/settings")]
public class PlatformSettingsController : ControllerBase
{
    private readonly IPlatformSettingsService _settings;
    public PlatformSettingsController(IPlatformSettingsService settings) => _settings = settings;

    // Any authenticated user can read the settings (buyers need to see the buyer fee).
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Get()
    {
        var r = await _settings.GetAsync();
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPut]
    [Authorize(Policy = "StaffOnly")]
    public async Task<IActionResult> Update([FromBody] UpdatePlatformSettingsRequest request)
    {
        var r = await _settings.UpdateAsync(request);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }
}
