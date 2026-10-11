using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stallions.Server.TaxDocuments;
using Stallions.Shared.DTOs.TaxDocuments;

namespace Stallions.Server.Controllers;

[ApiController]
[Route("api/tax-documents")]
public class TaxDocumentsController : ControllerBase
{
    private readonly ITaxDocumentQueryService _documents;
    public TaxDocumentsController(ITaxDocumentQueryService documents) => _documents = documents;

    // Any signed-in user; the service returns 404 unless the caller owns the recipient or is Staff.
    [HttpGet("{id:guid}")]
    [Authorize]
    public async Task<IActionResult> Get(Guid id)
    {
        var r = await _documents.GetAsync(id);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpGet("mine")]
    [Authorize(Policy = "BuyerOnly")]
    public async Task<IActionResult> GetMine()
    {
        var r = await _documents.GetMineAsync();
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpGet("stud")]
    [Authorize(Policy = "StudFarmAdminOnly")]
    public async Task<IActionResult> GetForStud()
    {
        var r = await _documents.GetForStudAsync();
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpGet]
    [Authorize(Policy = "StaffOnly")]
    public async Task<IActionResult> GetRegister([FromQuery] TaxDocumentFilter filter)
    {
        var r = await _documents.GetRegisterAsync(filter);
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }

    [HttpGet("export.csv")]
    [Authorize(Policy = "StaffOnly")]
    public async Task<IActionResult> Export([FromQuery] TaxDocumentFilter filter)
    {
        var r = await _documents.ExportCsvAsync(filter);
        if (!r.Succeeded) return StatusCode(r.HttpStatusCode, r.Error);
        // UTF-8 with a BOM so Excel shows "—" and other non-ASCII text correctly.
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(r.Value!)).ToArray();
        return File(bytes, "text/csv; charset=utf-8", "tax-documents.csv");
    }

    [HttpPost("issue-missing")]
    [Authorize(Policy = "StaffOnly")]
    public async Task<IActionResult> IssueMissing()
    {
        var r = await _documents.IssueMissingAsync();
        return r.Succeeded ? Ok(r.Value) : StatusCode(r.HttpStatusCode, r.Error);
    }
}
