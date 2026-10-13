namespace Stallions.Shared.DTOs.ChangeLog;

/// <summary>One changed field. Long text (wording, terms) has no values — the entry's Link opens the version.</summary>
public class ChangeLogChangeDto
{
    public string Field { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public bool IsLongText { get; set; }
}
