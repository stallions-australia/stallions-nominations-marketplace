using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.Enquiries;

namespace Stallions.Server.Controllers;

[ApiController]
[Authorize]
public class EnquiriesController : ControllerBase
{
    private readonly IEnquiryService _enquiries;
    public EnquiriesController(IEnquiryService enquiries) => _enquiries = enquiries;

    [HttpPost("api/listings/{id:guid}/enquiries")]
    [Authorize(Roles = "Buyer")]
    public async Task<IActionResult> Create(Guid id, [FromBody] OpenEnquiryRequest request)
    {
        var r = await _enquiries.CreateAsync(id, request);
        return r.Succeeded ? StatusCode(201, r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpGet("api/enquiries")]
    public async Task<IActionResult> GetAll()
    {
        var r = await _enquiries.GetAllForCallerAsync();
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpGet("api/enquiries/{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var r = await _enquiries.GetByIdAsync(id);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost("api/enquiries/{id:guid}/messages")]
    public async Task<IActionResult> PostMessage(Guid id, [FromBody] SendMessageRequest request)
    {
        var r = await _enquiries.PostMessageAsync(id, request);
        return r.Succeeded ? StatusCode(201, r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost("api/enquiries/{id:guid}/close")]
    [Authorize]
    public async Task<IActionResult> Close(Guid id)
    {
        var r = await _enquiries.CloseAsync(id);
        return r.Succeeded ? NoContent() : StatusCode(r.HttpStatusCode, r.Error);
    }
}
