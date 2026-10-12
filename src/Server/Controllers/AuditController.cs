using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.ChangeLog;

namespace Stallions.Server.Controllers;

[ApiController]
[Route("api/audit")]
[Authorize(Policy = "StaffOnly")]
public class AuditController : ControllerBase
{
    private readonly IChangeLogService _changeLog;
    public AuditController(IChangeLogService changeLog) => _changeLog = changeLog;

    /// <summary>The Staff change log. from / to are Sydney dates (yyyy-MM-dd); page is 1-based.</summary>
    [HttpGet("changes")]
    public async Task<IActionResult> GetChanges([FromQuery] string? area, [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to, [FromQuery] int page = 1)
    {
        var r = await _changeLog.GetAsync(new ChangeLogFilter { Area = area, From = from, To = to, Page = page });
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }
}
