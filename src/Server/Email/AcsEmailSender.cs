using Azure;
using Azure.Communication.Email;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace Stallions.Server.Email;

/// <summary>
/// Azure Communication Services, authenticated with the App Service's managed identity.
/// Returns once ACS has accepted the email; delivery happens asynchronously at ACS.
/// </summary>
public class AcsEmailSender : IEmailSender
{
    private readonly EmailClient _client;
    private readonly string _sender;

    public AcsEmailSender(IOptions<EmailOptions> options)
    {
        _client = new EmailClient(new Uri(options.Value.AcsEndpoint), new DefaultAzureCredential());
        _sender = options.Value.SenderAddress;
    }

    public async Task SendAsync(OutgoingEmail email, CancellationToken ct)
    {
        var content = new EmailContent(email.Subject) { Html = email.HtmlBody, PlainText = email.TextBody };
        var message = new EmailMessage(_sender, email.ToAddress, content);
        await _client.SendAsync(WaitUntil.Started, message, ct);
    }
}
