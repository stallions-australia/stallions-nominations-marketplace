using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stallions.Server.Content;
using Stallions.Server.Services;
using Stallions.Shared.DTOs.Content;

namespace Stallions.Server.Controllers;

/// <summary>Staff → Wording: the disclosures and every system email's subject and message.</summary>
[ApiController]
[Route("api/content")]
[Authorize(Policy = "StaffOnly")]
public class ContentController : ControllerBase
{
    private readonly IContentService _content;
    public ContentController(IContentService content) => _content = content;

    [HttpGet]
    public async Task<IActionResult> List() => Result(await _content.ListAsync());

    [HttpGet("{key}")]
    public async Task<IActionResult> Get(string key) => Result(await _content.GetDetailAsync(key));

    [HttpGet("{key}/versions")]
    public async Task<IActionResult> Versions(string key) => Result(await _content.GetHistoryAsync(key));

    [HttpPut("{key}")]
    public async Task<IActionResult> Save(string key, [FromBody] SaveContentRequest request) =>
        Result(await _content.SaveAsync(key, request));

    [HttpPost("{key}/versions/{version:int}/restore")]
    public async Task<IActionResult> Restore(string key, int version) => Result(await _content.RestoreAsync(key, version));

    [HttpPost("{key}/reset")]
    public async Task<IActionResult> Reset(string key) => Result(await _content.ResetAsync(key));

    [HttpPost("{key}/preview")]
    public async Task<IActionResult> Preview(string key, [FromBody] SaveContentRequest request) =>
        Result(await _content.PreviewAsync(key, request));

    private IActionResult Result<T>(ServiceResult<T> r) => r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
}
