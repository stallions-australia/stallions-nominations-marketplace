// tests/Server.Tests/Helpers/FakeTaxDocumentNumberSource.cs
using Stallions.Server.TaxDocuments;

namespace Stallions.Server.Tests.Helpers;

/// <summary>The in-memory provider can't run SQL sequences: a counter stands in for TaxDocumentNumbers.</summary>
public class FakeTaxDocumentNumberSource : ITaxDocumentNumberSource
{
    private long _last;
    public void StartAt(long first) => _last = first - 1;
    public Task<long> NextAsync() => Task.FromResult(Interlocked.Increment(ref _last));
}
