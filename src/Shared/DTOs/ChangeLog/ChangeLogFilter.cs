namespace Stallions.Shared.DTOs.ChangeLog;

/// <summary>From / To are Sydney calendar dates (inclusive). Page is 1-based, 50 entries a page.</summary>
public class ChangeLogFilter
{
    public string? Area { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public int Page { get; set; } = 1;
}
