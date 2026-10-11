using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Stallions.Server.Options;
using Stallions.Shared.DTOs.Checkout;

namespace Stallions.Server.Controllers;

[ApiController]
[Route("api/disclosures")]
public class DisclosuresController : ControllerBase
{
    private readonly IOptions<DisclosureOptions> _options;
    public DisclosuresController(IOptions<DisclosureOptions> options) => _options = options;

    // Public — shown on listing pages before a buyer signs in. Wording comes from configuration.
    [HttpGet("buyer-fee")]
    [AllowAnonymous]
    public IActionResult GetBuyerFee() => Ok(new BuyerFeeDisclosureDto
    {
        BuyerFeeExplanation = _options.Value.BuyerFeeExplanation,
        BalanceArrangement = _options.Value.StudFarmBalanceArrangement,
        SavedCardExplanation = _options.Value.SavedCardExplanation
    });
}
