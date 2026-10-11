// src/Server/TaxDocuments/TaxDocumentNumberSource.cs
using Microsoft.EntityFrameworkCore;
using Stallions.Server.Data;

namespace Stallions.Server.TaxDocuments;

/// <summary>The next tax document number. Tests use a counter (the in-memory provider has no sequences).</summary>
public interface ITaxDocumentNumberSource
{
    Task<long> NextAsync();
}

/// <summary>
/// SQL sequence dbo.TaxDocumentNumbers: safe across app instances. Runs on the context's connection, so it
/// joins the caller's transaction; a rolled-back transaction leaves a gap, which is acceptable.
/// </summary>
public class SqlTaxDocumentNumberSource : ITaxDocumentNumberSource
{
    private readonly AppDbContext _db;
    public SqlTaxDocumentNumberSource(AppDbContext db) => _db = db;

    // ToListAsync (not SingleAsync) so EF sends the SQL as written: NEXT VALUE FOR can't sit in a subquery.
    public async Task<long> NextAsync() =>
        (await _db.Database.SqlQuery<long>($"SELECT NEXT VALUE FOR [dbo].[TaxDocumentNumbers] AS [Value]").ToListAsync()).Single();
}
