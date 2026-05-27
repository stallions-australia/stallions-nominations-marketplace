using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Stallions.Server.Controllers;

/// <summary>
/// Temporary debug endpoint — remove before production hardening.
/// Navigating to /api/auth/debug while signed in shows exactly what
/// claims the server sees in the JWT, making auth issues diagnosable.
/// </summary>
[ApiController]
[Route("api/auth")]
[Authorize]
public class AuthDebugController : ControllerBase
{
    [HttpGet("debug")]
    public IActionResult Debug()
    {
        var claims = User.Claims.Select(c => new { c.Type, c.Value }).ToList();
        var roles  = User.Claims
            .Where(c => c.Type is "roles"
                     or "role"
                     or "http://schemas.microsoft.com/ws/2008/06/identity/claims/role")
            .Select(c => c.Value)
            .ToList();

        var name = User.Identity?.Name
            ?? User.Claims.FirstOrDefault(c => c.Type == "name")?.Value;

        return Ok(new
        {
            IsAuthenticated = User.Identity?.IsAuthenticated,
            Name            = name,
            IsStaff         = User.IsInRole("Staff"),
            IsStudFarmAdmin = User.IsInRole("StudFarmAdmin"),
            IsBuyer         = User.IsInRole("Buyer"),
            Roles           = roles,
            AllClaims       = claims
        });
    }
}
