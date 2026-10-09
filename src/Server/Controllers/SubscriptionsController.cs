using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.Subscriptions;

namespace Stallions.Server.Controllers;

[ApiController]
[Route("api/subscriptions")]
public class SubscriptionsController : ControllerBase
{
    private readonly ISubscriptionService _subscriptions;
    public SubscriptionsController(ISubscriptionService subscriptions) => _subscriptions = subscriptions;

    [HttpGet]
    [Authorize(Policy = "StaffOnly")]
    public async Task<IActionResult> GetAll([FromQuery] Guid? seasonId, [FromQuery] string? status)
    {
        var r = await _subscriptions.GetAllAsync(seasonId, status);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpGet("mine")]
    [Authorize(Policy = "StudFarmAdminOnly")]
    public async Task<IActionResult> GetMine()
    {
        var r = await _subscriptions.GetForStudFarmAsync();
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost]
    [Authorize(Policy = "StaffOnly")]
    public async Task<IActionResult> Create([FromBody] CreateSubscriptionRequest request)
    {
        var r = await _subscriptions.CreateAsync(request);
        return r.Succeeded ? StatusCode(201, r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost("{id:guid}/mark-paid")]
    [Authorize(Policy = "StaffOnly")]
    public async Task<IActionResult> MarkPaid(Guid id, [FromBody] MarkSubscriptionPaidRequest request)
    {
        var r = await _subscriptions.MarkPaidAsync(id, request);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost("{id:guid}/waive")]
    [Authorize(Policy = "StaffOnly")]
    public async Task<IActionResult> Waive(Guid id, [FromBody] WaiveSubscriptionRequest request)
    {
        var r = await _subscriptions.WaiveAsync(id, request);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }
}
