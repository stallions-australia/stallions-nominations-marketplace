namespace Stallions.Shared.DTOs.Terms;

/// <summary>The current stud listing terms and whether the caller's farm still has to accept them.</summary>
public class StudTermsStatusDto
{
    /// <summary>Null when no stud listing terms are published (then nothing is gated).</summary>
    public TermsDocumentDto? Current { get; set; }
    public int? AcceptedVersion { get; set; }
    public DateTime? AcceptedAt { get; set; }
    /// <summary>True for a stud admin whose farm hasn't accepted the current version; always false for Staff.</summary>
    public bool NeedsAcceptance { get; set; }
}
