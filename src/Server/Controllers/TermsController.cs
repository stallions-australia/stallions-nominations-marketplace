using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.Terms;

namespace Stallions.Server.Controllers;

[ApiController]
[Route("api/terms")]
public class TermsController : ControllerBase
{
    private readonly ITermsService _terms;
    public TermsController(ITermsService terms) => _terms = terms;

    // Any authenticated user can read the current buyer T&C (buyers need it to accept).
    [HttpGet("current")]
    [Authorize]
    public async Task<IActionResult> GetCurrent()
    {
        var r = await _terms.GetCurrentAsync();
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost]
    [Authorize(Policy = "StaffOnly")]
    public async Task<IActionResult> Publish([FromBody] PublishTermsRequest request)
    {
        var r = await _terms.PublishAsync(request);
        return r.Succeeded ? StatusCode(201, r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpGet("history")]
    [Authorize(Policy = "StaffOnly")]
    public async Task<IActionResult> GetHistory([FromQuery] string? audience)
    {
        var r = await _terms.GetHistoryAsync(audience);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    // The stud's own acceptance status (Staff see the current terms only).
    [HttpGet("stud/current")]
    [Authorize(Policy = "StudFarmOrStaff")]
    public async Task<IActionResult> GetStudStatus()
    {
        var r = await _terms.GetStudStatusAsync();
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost("stud/accept")]
    [Authorize(Policy = "StudFarmAdminOnly")]
    public async Task<IActionResult> AcceptStud([FromBody] AcceptTermsRequest request)
    {
        var r = await _terms.AcceptStudTermsAsync(request);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }
}
