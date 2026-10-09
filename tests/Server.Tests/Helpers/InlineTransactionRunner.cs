using Stallions.Server.Data;

namespace Stallions.Server.Tests.Helpers;

/// <summary>Runs the work directly; the in-memory test database has no transactions.</summary>
public class InlineTransactionRunner : ITransactionRunner
{
    public Task<T> RunAsync<T>(Func<Task<T>> work) => work();
}
