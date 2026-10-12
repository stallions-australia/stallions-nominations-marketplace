using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Stallions.Server.Content;
using Stallions.Server.Controllers;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.Content;

namespace Stallions.Server.Tests.Controllers;

public class ContentControllerTests
{
    [Fact]
    public void TheWordingApi_IsStaffOnly() =>
        typeof(ContentController).GetCustomAttribute<AuthorizeAttribute>()!.Policy.Should().Be("StaffOnly");

    [Fact]
    public async Task Save_WhenTheServiceRejects_ReturnsItsStatusAndMessage()
    {
        var content = new Mock<IContentService>();
        content.Setup(c => c.SaveAsync("Email.Outbid", It.IsAny<SaveContentRequest>()))
            .ReturnsAsync(ServiceResult<ContentItemDetailDto>.BadRequest("The message must include {HighBid}."));

        var result = await new ContentController(content.Object).Save("Email.Outbid", new SaveContentRequest { Body = "x" }) as ObjectResult;

        result!.StatusCode.Should().Be(400);
        result.Value.Should().Be("The message must include {HighBid}.");
    }
}
