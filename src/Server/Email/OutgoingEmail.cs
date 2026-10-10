namespace Stallions.Server.Email;

/// <summary>A composed email. Template names the kind of email, e.g. "WonAndCharged".</summary>
public sealed record OutgoingEmail(string ToAddress, string Subject, string HtmlBody, string TextBody, string Template);
