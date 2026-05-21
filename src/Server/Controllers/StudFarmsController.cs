using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stallions.Server.Data.Repositories;
using Stallions.Shared.DTOs.StudFarms;

namespace Stallions.Server.Controllers;

[ApiController]
[Route("api/studfarms")]
[AllowAnonymous]
public class StudFarmsController : ControllerBase
{
    private readonly IStudFarmRepository _farms;
    public StudFarmsController(IStudFarmRepository farms) => _farms = farms;

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var farm = await _farms.GetByIdAsync(id);
        if (farm == null) return NotFound();
        return Ok(new StudFarmDto
        {
            Id           = farm.Id,
            Name         = farm.Name,
            Address      = farm.Address,
            ContactEmail = farm.ContactEmail,
            ContactPhone = farm.ContactPhone
        });
    }
}
