using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stallions.Server.Payments;
using Stallions.Server.Services;

namespace Stallions.Server.Controllers;

[ApiController]
[Route("api/payments")]
public class PaymentsController : ControllerBase
{
    private readonly ICardService _cards;
    private readonly IPaymentProvider _provider;
    private readonly IPaymentEventProcessor _processor;
    private readonly ILogger<PaymentsController> _logger;

    public PaymentsController(ICardService cards, IPaymentProvider provider, IPaymentEventProcessor processor,
        ILogger<PaymentsController> logger)
    {
        _cards = cards;
        _provider = provider;
        _processor = processor;
        _logger = logger;
    }

    [HttpGet("card")]
    [Authorize(Policy = "BuyerOnly")]
    public async Task<IActionResult> GetCard()
    {
        var r = await _cards.GetMineAsync();
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpPost("card/setup-session")]
    [Authorize(Policy = "BuyerOnly")]
    public async Task<IActionResult> StartCardSetup()
    {
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var r = await _cards.StartSetupAsync($"{baseUrl}/account/card?result=success", $"{baseUrl}/account/card?result=cancelled");
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    // Public, but nothing is processed unless the provider's signature verifies. Processing
    // errors propagate as 500 so Stripe retries; duplicates are ignored by the processor.
    [HttpPost("webhook/stripe")]
    [AllowAnonymous]
    [RequestSizeLimit(262_144)]
    public async Task<IActionResult> StripeWebhook()
    {
        if (_provider.Name != PaymentOptions.ProviderStripe) return NotFound();

        using var reader = new StreamReader(Request.Body);
        var rawBody = await reader.ReadToEndAsync(HttpContext.RequestAborted);

        PaymentEvent paymentEvent;
        try
        {
            paymentEvent = await _provider.ParseWebhookAsync(rawBody, Request.Headers["Stripe-Signature"].FirstOrDefault());
        }
        catch (PaymentSignatureException ex)
        {
            _logger.LogWarning("Stripe webhook signature verification failed: {Reason}", ex.Message);
            return BadRequest("Invalid signature.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stripe webhook could not be parsed");
            throw;
        }

        var outcome = await _processor.ProcessAsync(paymentEvent);
        // Another delivery of this event is mid-processing: a non-2xx makes Stripe retry later.
        return outcome == PaymentEventOutcome.InProgress
            ? StatusCode(StatusCodes.Status409Conflict, "Event is still being processed; retry later.")
            : Ok();
    }
}
