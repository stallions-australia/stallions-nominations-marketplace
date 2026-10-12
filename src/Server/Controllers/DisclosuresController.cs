using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stallions.Server.Content;
using Stallions.Shared.DTOs.Checkout;

namespace Stallions.Server.Controllers;

[ApiController]
[Route("api/disclosures")]
public class DisclosuresController : ControllerBase
{
    private readonly IContentService _content;
    public DisclosuresController(IContentService content) => _content = content;

    // Public — shown on listing pages before a buyer signs in. Wording is Staff-managed (Staff → Wording).
    [HttpGet("buyer-fee")]
    [AllowAnonymous]
    public async Task<IActionResult> GetBuyerFee() => Ok(new BuyerFeeDisclosureDto
    {
        BuyerFeeExplanation = (await _content.GetAsync(ContentKeys.BuyerFeeExplanation)).Body,
        BalanceArrangement = (await _content.GetAsync(ContentKeys.StudFarmBalanceArrangement)).Body,
        SavedCardExplanation = (await _content.GetAsync(ContentKeys.SavedCardExplanation)).Body
    });
}
