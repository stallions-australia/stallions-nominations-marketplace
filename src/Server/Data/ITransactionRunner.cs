using Microsoft.EntityFrameworkCore;

namespace Stallions.Server.Data;

/// <summary>Runs a unit of work in one database transaction (all saves commit together or not at all).</summary>
public interface ITransactionRunner
{
    Task<T> RunAsync<T>(Func<Task<T>> work);
}

public class EfTransactionRunner : ITransactionRunner
{
    private readonly AppDbContext _db;
    public EfTransactionRunner(AppDbContext db) => _db = db;

    public Task<T> RunAsync<T>(Func<Task<T>> work)
    {
        // The context uses EnableRetryOnFailure, so the transaction must run inside the execution
        // strategy; the work re-reads fresh state on each attempt.
        var strategy = _db.Database.CreateExecutionStrategy();
        return strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            var result = await work();
            await tx.CommitAsync();
            return result;
        });
    }
}
