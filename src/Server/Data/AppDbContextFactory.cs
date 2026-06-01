using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Stallions.Server.Data;

/// <summary>
/// Design-time factory for EF Core migrations.
/// Used by `dotnet ef` tooling when a live connection string is not available.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=StallionsNominations_DesignTime;Trusted_Connection=True;MultipleActiveResultSets=true")
            .Options;

        return new AppDbContext(options);
    }
}
