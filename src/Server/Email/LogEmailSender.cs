namespace Stallions.Server.Email;

/// <summary>Local runs and tests: writes the email to the log instead of sending it.</summary>
public class LogEmailSender : IEmailSender
{
    private readonly ILogger<LogEmailSender> _log;

    public LogEmailSender(ILogger<LogEmailSender> log) => _log = log;

    public Task SendAsync(OutgoingEmail email, CancellationToken ct)
    {
        _log.LogInformation("Email ({Template}) to {To}: {Subject}\n{Body}",
            email.Template, email.ToAddress, email.Subject, email.TextBody);
        return Task.CompletedTask;
    }
}
