using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stallions.Server.Payments;

namespace Stallions.Server.Controllers;

/// <summary>
/// The fake provider's "hosted page". Returns 404 unless Payments:Provider = Fake, which the
/// startup guard refuses in Production. Session ids are unguessable and single-use.
/// </summary>
[ApiController]
[Route("payments/fake")]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
public class FakePaymentController : ControllerBase
{
    private readonly IPaymentProvider _provider;
    private readonly IPaymentEventProcessor _processor;

    public FakePaymentController(IPaymentProvider provider, IPaymentEventProcessor processor)
    {
        _provider = provider;
        _processor = processor;
    }

    [HttpGet("{id}")]
    public IActionResult Show(string id)
    {
        if (_provider is not FakePaymentProvider fake || fake.GetSession(id) is not { } s) return NotFound();
        var what = s.IsCardSetup
            ? "Save a card (simulated Visa •••• 4242)"
            : $"{WebUtility.HtmlEncode(s.Description)} — {s.AmountCents / 100m:C} AUD";
        var html = $$"""
            <!doctype html><html lang="en"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Simulated payment</title>
            <style>body{font-family:system-ui,sans-serif;max-width:480px;margin:48px auto;padding:0 16px}
            .note{background:#fff8e1;padding:12px;border-radius:8px}button{padding:10px 18px;margin-right:8px}</style>
            </head><body>
            <h1>Simulated payment</h1>
            <p class="note">Dev only — no real card is charged. Stripe replaces this page once its keys are configured.</p>
            <p><strong>{{what}}</strong></p>
            <form method="post" action="/payments/fake/{{id}}/approve" style="display:inline"><button type="submit">Approve</button></form>
            <form method="post" action="/payments/fake/{{id}}/decline" style="display:inline"><button type="submit">Decline</button></form>
            </body></html>
            """;
        return Content(html, "text/html");
    }

    [HttpPost("{id}/approve")]
    public async Task<IActionResult> Approve(string id)
    {
        if (_provider is not FakePaymentProvider fake || fake.Approve(id) is not { } result) return NotFound();
        await _processor.ProcessAsync(result.Event);
        return Redirect(result.RedirectUrl);
    }

    [HttpPost("{id}/decline")]
    public IActionResult Decline(string id)
    {
        if (_provider is not FakePaymentProvider fake || fake.Decline(id) is not { } cancelUrl) return NotFound();
        return Redirect(cancelUrl);
    }
}
