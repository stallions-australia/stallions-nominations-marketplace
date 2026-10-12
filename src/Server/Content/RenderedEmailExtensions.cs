// src/Server/Content/RenderedEmailExtensions.cs
namespace Stallions.Server.Content;

public static class RenderedEmailExtensions
{
    /// <summary>A substituted value (stallion name, reason...) must never add a header line to the subject.</summary>
    public static RenderedEmail WithSingleLineSubject(this RenderedEmail email) =>
        email with { Subject = email.Subject.Replace('\r', ' ').Replace('\n', ' ') };
}
