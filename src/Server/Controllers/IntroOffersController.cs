using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.IntroOffers;

namespace Stallions.Server.Controllers;

[ApiController]
[Route("api/intro-offers")]
[Authorize(Policy = "StaffOnly")]
public class IntroOffersController : ControllerBase
{
    private readonly IIntroOfferService _offers;
    public IntroOffersController(IIntroOfferService offers) => _offers = offers;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var r = await _offers.GetAllAsync();
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpGet("preview")]
    public async Task<IActionResult> Preview([FromQuery] Guid studFarmId, [FromQuery] Guid seasonId)
    {
        var r = await _offers.PreviewAsync(studFarmId, seasonId);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveIntroOfferRequest request)
    {
        var r = await _offers.CreateAsync(request);
        return r.Succeeded ? StatusCode(201, r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveIntroOfferRequest request)
    {
        var r = await _offers.UpdateAsync(id, request);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost("{id:guid}/retire")]
    public async Task<IActionResult> Retire(Guid id)
    {
        var r = await _offers.RetireAsync(id);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }
}
