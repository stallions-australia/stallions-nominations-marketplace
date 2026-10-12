namespace Stallions.Shared.DTOs.ChangeLog;

public class ChangeLogEntryDto
{
    public long Id { get; set; }
    /// <summary>UTC.</summary>
    public DateTime OccurredAt { get; set; }
    /// <summary>Sydney time, e.g. "12 Oct 2026, 2:00 pm".</summary>
    public string When { get; set; } = string.Empty;
    public string Who { get; set; } = string.Empty;
    public string Area { get; set; } = string.Empty;
    public string AreaLabel { get; set; } = string.Empty;
    public string Item { get; set; } = string.Empty;
    public string? Note { get; set; }
    /// <summary>A Staff page showing the version (wording, terms).</summary>
    public string? Link { get; set; }
    public List<ChangeLogChangeDto> Changes { get; set; } = [];
}
