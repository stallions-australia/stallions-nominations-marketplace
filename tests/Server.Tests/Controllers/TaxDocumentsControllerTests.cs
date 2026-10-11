using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Stallions.Server.Controllers;
using Stallions.Server.Services;
using Stallions.Server.TaxDocuments;
using Stallions.Shared.DTOs.TaxDocuments;

namespace Stallions.Server.Tests.Controllers;

public class TaxDocumentsControllerTests
{
    private readonly Mock<ITaxDocumentQueryService> _documents = new();

    [Fact]
    public async Task Export_ReturnsAUtf8CsvFileWithABom()
    {
        _documents.Setup(d => d.ExportCsvAsync(It.IsAny<TaxDocumentFilter>())).ReturnsAsync(ServiceResult<string>.Ok("Number\r\n"));

        var result = await new TaxDocumentsController(_documents.Object).Export(new TaxDocumentFilter());

        var file = result.Should().BeOfType<FileContentResult>().Subject;
        file.ContentType.Should().Be("text/csv; charset=utf-8");
        file.FileDownloadName.Should().Be("tax-documents.csv");
        file.FileContents.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
    }

    [Fact]
    public async Task Get_SomeoneElsesDocument_Is404()
    {
        _documents.Setup(d => d.GetAsync(It.IsAny<Guid>())).ReturnsAsync(ServiceResult<TaxDocumentDto>.NotFound("Tax document not found."));

        var result = await new TaxDocumentsController(_documents.Object).Get(Guid.NewGuid());

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(404);
    }
}
