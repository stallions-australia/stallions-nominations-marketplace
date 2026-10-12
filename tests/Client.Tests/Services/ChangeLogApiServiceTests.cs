using FluentAssertions;
using Stallions.Client.Services;
using Stallions.Shared.DTOs.ChangeLog;

namespace Stallions.Client.Tests.Services;

public class ChangeLogApiServiceTests
{
    [Fact]
    public void BuildQuery_IncludesTheFiltersSet_AndThePage()
    {
        ChangeLogApiService.BuildQuery(new ChangeLogFilter()).Should().Be("?page=1");
        ChangeLogApiService.BuildQuery(new ChangeLogFilter
        {
            Area = "Wording", From = new DateOnly(2026, 10, 1), To = new DateOnly(2026, 10, 31), Page = 2
        }).Should().Be("?area=Wording&from=2026-10-01&to=2026-10-31&page=2");
    }
}
