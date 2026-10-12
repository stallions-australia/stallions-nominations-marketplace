namespace Stallions.Shared.DTOs.ChangeLog;

public class ChangeLogPageDto
{
    public List<ChangeLogEntryDto> Entries { get; set; } = [];
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int Total { get; set; }
    public int TotalPages => PageSize <= 0 ? 0 : Math.Max(1, (Total + PageSize - 1) / PageSize);
}
